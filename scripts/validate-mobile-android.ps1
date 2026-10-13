[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Write-Stage {
    param([Parameter(Mandatory = $true)][string]$Message)

    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @()
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($ArgumentList -join ' ')"
    }
}

function Get-JavaMajorVersion {
    param([Parameter(Mandatory = $true)][string]$JavaPath)

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $JavaPath
    $startInfo.Arguments = '-version'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $versionText = $process.StandardOutput.ReadToEnd() + $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne 0 -or $versionText -notmatch 'version\s+"(?:1\.)?(\d+)') {
        return $null
    }

    return [int]$Matches[1]
}

function Find-Jdk17 {
    $candidateHomes = New-Object System.Collections.Generic.List[string]

    foreach ($scope in @('User', 'Process', 'Machine')) {
        $configuredHome = [Environment]::GetEnvironmentVariable('JAVA_HOME', $scope)
        if ($configuredHome) { $candidateHomes.Add($configuredHome) }
    }

    $patterns = @(
        (Join-Path $env:ProgramFiles 'Eclipse Adoptium\jdk-17*'),
        (Join-Path $env:ProgramFiles 'Microsoft\jdk-17*'),
        (Join-Path $env:ProgramFiles 'Java\jdk-17*'),
        (Join-Path $env:ProgramFiles 'Amazon Corretto\jdk17*'),
        (Join-Path $env:ProgramFiles 'Android\Android Studio\jbr')
    )

    foreach ($pattern in $patterns) {
        Get-Item -Path $pattern -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            ForEach-Object { $candidateHomes.Add($_.FullName) }
    }

    foreach ($candidateHome in ($candidateHomes | Select-Object -Unique)) {
        $javaPath = Join-Path $candidateHome 'bin\java.exe'
        $javacPath = Join-Path $candidateHome 'bin\javac.exe'
        if (-not (Test-Path -LiteralPath $javaPath) -or -not (Test-Path -LiteralPath $javacPath)) {
            continue
        }

        if ((Get-JavaMajorVersion -JavaPath $javaPath) -eq 17) {
            return (Resolve-Path -LiteralPath $candidateHome).Path
        }
    }

    throw 'JDK 17 was not found. Set JAVA_HOME to an existing JDK 17 installation and run this script again.'
}

function Test-AndroidSdk {
    param([Parameter(Mandatory = $true)][string]$SdkPath)

    return (
        (Test-Path -LiteralPath $SdkPath -PathType Container) -and
        (Test-Path -LiteralPath (Join-Path $SdkPath 'platform-tools\adb.exe') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $SdkPath 'platforms') -PathType Container) -and
        (Test-Path -LiteralPath (Join-Path $SdkPath 'build-tools') -PathType Container)
    )
}

function Get-PhysicalSdkPath {
    param([Parameter(Mandatory = $true)][string]$SdkPath)

    # Resolve through a file handle: Resolve-Path alone misses MSIX AppData redirection.
    if (-not ('MobileValidation.NativePaths' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace MobileValidation {
    public static class NativePaths {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetFinalPathNameByHandle(IntPtr handle, StringBuilder path, uint size, uint flags);
    }
}
'@
    }
    $file = [IO.File]::OpenRead((Join-Path $SdkPath 'platform-tools\adb.exe'))
    try {
        $buffer = New-Object Text.StringBuilder 32768
        $length = [MobileValidation.NativePaths]::GetFinalPathNameByHandle(
            $file.SafeFileHandle.DangerousGetHandle(), $buffer, 32768, 0)
        if ($length -eq 0 -or $length -ge 32768) {
            throw 'Cannot resolve the physical SDK path. Run validation outside the restricted sandbox.'
        }
        $physicalAdb = $buffer.ToString()
        if ($physicalAdb.StartsWith('\\?\UNC\')) {
            $physicalAdb = '\\' + $physicalAdb.Substring(8)
        } elseif ($physicalAdb.StartsWith('\\?\')) {
            $physicalAdb = $physicalAdb.Substring(4)
        }
        return Split-Path -Parent (Split-Path -Parent $physicalAdb)
    }
    finally { $file.Dispose() }
}

function Assert-AndroidProjectSdk {
    param([string]$SdkPath, [string]$AndroidRoot)

    $buildSettings = Get-Content -LiteralPath (Join-Path $AndroidRoot 'build.gradle') -Raw
    if ($buildSettings -notmatch 'compileSdkVersion\s*=\s*(\d+)') {
        throw 'Cannot determine compileSdkVersion from the Android project.'
    }
    $compileApi = $Matches[1]
    $matchingPlatforms = @(Get-ChildItem -LiteralPath (Join-Path $SdkPath 'platforms') -Directory |
        Where-Object {
            $propertiesPath = Join-Path $_.FullName 'source.properties'
            (Test-Path -LiteralPath (Join-Path $_.FullName 'android.jar')) -and
            (Test-Path -LiteralPath $propertiesPath) -and
            ((Get-Content -LiteralPath $propertiesPath -Raw) -match "(?m)^AndroidVersion.ApiLevel=$compileApi(?:\.0)?\s*$")
        })
    if ($matchingPlatforms.Count -eq 0) { throw "Android SDK Platform API $compileApi is missing." }
    foreach ($setting in @('buildToolsVersion', 'ndkVersion', 'cmakeVersion')) {
        if ($buildSettings -notmatch ($setting + '\s*=\s*"([^"]+)"')) {
            throw "Cannot determine $setting from the Android project."
        }
        $version = $Matches[1]
        $relativePath = if ($setting -eq 'buildToolsVersion') {
            "build-tools\$version\aapt2.exe"
        } elseif ($setting -eq 'ndkVersion') { "ndk\$version\source.properties" }
        else { "cmake\$version\bin\cmake.exe" }
        if (-not (Test-Path -LiteralPath (Join-Path $SdkPath $relativePath) -PathType Leaf)) {
            throw "Required Android package is missing: $setting $version. Install it with SDK Manager."
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $SdkPath 'cmdline-tools\latest\bin\sdkmanager.bat'))) {
        throw 'Android SDK Command-line Tools (latest) are missing.'
    }
}

function Find-AndroidSdk {
    $candidatePaths = New-Object System.Collections.Generic.List[string]

    foreach ($scope in @('User', 'Process', 'Machine')) {
        foreach ($name in @('ANDROID_HOME', 'ANDROID_SDK_ROOT')) {
            $configuredSdk = [Environment]::GetEnvironmentVariable($name, $scope)
            if ($configuredSdk) { $candidatePaths.Add($configuredSdk) }
        }
    }
    if ($env:LOCALAPPDATA) {
        $candidatePaths.Add((Join-Path $env:LOCALAPPDATA 'Android\Sdk'))
    }
    if ($env:USERPROFILE) {
        $candidatePaths.Add((Join-Path $env:USERPROFILE 'AppData\Local\Android\Sdk'))
        $candidatePaths.Add((Join-Path $env:USERPROFILE 'Android\Sdk'))
    }

    foreach ($candidatePath in ($candidatePaths | Select-Object -Unique)) {
        if (Test-AndroidSdk -SdkPath $candidatePath) {
            $physicalSdk = Get-PhysicalSdkPath -SdkPath $candidatePath
            if (Test-AndroidSdk -SdkPath $physicalSdk) { return $physicalSdk }
        }
    }

    throw 'Android SDK was not found. Set ANDROID_HOME or ANDROID_SDK_ROOT to an existing SDK and run this script again.'
}

function Find-NodeToolchain {
    # npm/npx Windows launchers use the adjacent node.exe. Keep all three together.
    $candidates = @(Get-Command node.exe -CommandType Application -All -ErrorAction SilentlyContinue)
    foreach ($candidate in $candidates) {
        $nodePath = $candidate.Source
        if (-not [IO.Path]::IsPathRooted($nodePath) -or
            -not (Test-Path -LiteralPath $nodePath -PathType Leaf)) { continue }
        $nodeDirectory = Split-Path -Parent $nodePath
        $npmPath = Join-Path $nodeDirectory 'npm.cmd'
        $npxPath = Join-Path $nodeDirectory 'npx.cmd'
        if ((Test-Path -LiteralPath $npmPath -PathType Leaf) -and
            (Test-Path -LiteralPath $npxPath -PathType Leaf)) {
            return [pscustomobject]@{
                Node = (Resolve-Path -LiteralPath $nodePath).Path
                Npm = (Resolve-Path -LiteralPath $npmPath).Path
                Npx = (Resolve-Path -LiteralPath $npxPath).Path
                Directory = $nodeDirectory
            }
        }
    }
    throw 'A matching Node/npm/npx installation was not found on PATH.'
}

try {
    $repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
    $mobileRoot = Join-Path $repositoryRoot 'src\Mobile\ExamFollowerStudent'
    $androidRoot = Join-Path $mobileRoot 'android'
    $packageJson = Join-Path $mobileRoot 'package.json'
    $gradleWrapper = Join-Path $androidRoot 'gradlew.bat'

    if (-not (Test-Path -LiteralPath $packageJson -PathType Leaf) -or
        -not (Test-Path -LiteralPath $gradleWrapper -PathType Leaf)) {
        throw "The expected mobile project was not found under $mobileRoot."
    }

    Write-Stage 'Locating JDK 17 and Android SDK'
    $jdkHome = Find-Jdk17
    $androidSdk = Find-AndroidSdk
    $nodeToolchain = Find-NodeToolchain
    Assert-AndroidProjectSdk -SdkPath $androidSdk -AndroidRoot $androidRoot
    $packageSettings = Get-Content -LiteralPath $packageJson -Raw | ConvertFrom-Json
    if ($packageSettings.engines.node -notmatch '^>=\s*(\d+\.\d+\.\d+)$') {
        throw 'Cannot determine the minimum Node version from package.json.'
    }
    $minimumNode = [version]$Matches[1]
    $installedNode = & $nodeToolchain.Node '-p' 'process.versions.node'
    if ($LASTEXITCODE -ne 0 -or [version]$installedNode -lt $minimumNode) {
        throw "Node $minimumNode or newer is required."
    }
    $java = Join-Path $jdkHome 'bin\java.exe'
    $javac = Join-Path $jdkHome 'bin\javac.exe'
    $adb = Join-Path $androidSdk 'platform-tools\adb.exe'

    $env:JAVA_HOME = $jdkHome
    $env:ANDROID_HOME = $androidSdk
    $env:ANDROID_SDK_ROOT = $androidSdk
    $env:GRADLE_USER_HOME = Join-Path $mobileRoot '.gradle-user-home'
    $validationTemp = Join-Path $mobileRoot '.tmp\validation'
    [void](New-Item -ItemType Directory -Path $validationTemp -Force)
    $env:TEMP = $validationTemp
    $env:TMP = $validationTemp
    $env:Path = $nodeToolchain.Directory + ';' + (Join-Path $jdkHome 'bin') + ';' + (Join-Path $androidSdk 'platform-tools') + ';' + $env:Path

    Write-Host "JDK 17: $jdkHome"
    Write-Host "Android SDK: $androidSdk"
    Write-Host "Gradle cache: $env:GRADLE_USER_HOME"

    Write-Stage 'Checking Node, npm, Java, javac, Android SDK, and adb versions'
    $npmExecutable = $nodeToolchain.Npm
    Write-Host "Node: $($nodeToolchain.Node)"
    Write-Host "npm: $npmExecutable"
    Write-Host "npx: $($nodeToolchain.Npx)"
    Invoke-CheckedCommand -FilePath $nodeToolchain.Node -ArgumentList @('--version')
    Invoke-CheckedCommand -FilePath $npmExecutable -ArgumentList @('--version')
    Invoke-CheckedCommand -FilePath $nodeToolchain.Npx -ArgumentList @('--version')
    Invoke-CheckedCommand -FilePath $java -ArgumentList @('-version')
    Invoke-CheckedCommand -FilePath $javac -ArgumentList @('-version')

    $platforms = Get-ChildItem -LiteralPath (Join-Path $androidSdk 'platforms') -Directory |
        Select-Object -ExpandProperty Name
    $buildTools = Get-ChildItem -LiteralPath (Join-Path $androidSdk 'build-tools') -Directory |
        Select-Object -ExpandProperty Name
    if (-not $platforms -or -not $buildTools) {
        throw 'The Android SDK does not contain both a platform and build-tools installation.'
    }
    Write-Host "Android platforms: $($platforms -join ', ')"
    Write-Host "Android build-tools: $($buildTools -join ', ')"
    Invoke-CheckedCommand -FilePath $adb -ArgumentList @('version')

    Write-Stage 'Checking existing mobile dependencies'
    $requiredDependencies = @(
        (Join-Path $mobileRoot 'node_modules'),
        (Join-Path $mobileRoot 'node_modules\.bin\tsc.cmd'),
        (Join-Path $mobileRoot 'node_modules\.bin\eslint.cmd'),
        (Join-Path $mobileRoot 'node_modules\.bin\jest.cmd')
    )
    foreach ($dependency in $requiredDependencies) {
        if (-not (Test-Path -LiteralPath $dependency)) {
            throw "A required mobile dependency is missing: $dependency. Run npm ci in the mobile project, then retry."
        }
    }
    Write-Host 'Existing mobile dependencies found.'

    Push-Location -LiteralPath $mobileRoot
    try {
        Write-Stage 'Running TypeScript type-check'
        Invoke-CheckedCommand -FilePath $npmExecutable -ArgumentList @('run', 'typecheck')

        Write-Stage 'Running ESLint'
        Invoke-CheckedCommand -FilePath $npmExecutable -ArgumentList @('run', 'lint')

        Write-Stage 'Running Jest tests'
        Invoke-CheckedCommand -FilePath $npmExecutable -ArgumentList @('test')
    }
    finally {
        Pop-Location
    }

    $gradleArguments = @(
        '--no-daemon',
        '--max-workers=1',
        '-Djava.net.preferIPv4Stack=true',
        '-Dkotlin.compiler.execution.strategy=in-process',
        '-Pkotlin.compiler.execution.strategy=in-process'
    )

    Push-Location -LiteralPath $androidRoot
    try {
        Write-Stage 'Running Android unit tests'
        Invoke-CheckedCommand -FilePath $gradleWrapper -ArgumentList (@('testDebugUnitTest') + $gradleArguments)

        Write-Stage 'Building Android debug APK'
        Invoke-CheckedCommand -FilePath $gradleWrapper -ArgumentList (@('assembleDebug') + $gradleArguments)
    }
    finally {
        Pop-Location
    }

    $debugApk = Join-Path $androidRoot 'app\build\outputs\apk\debug\app-debug.apk'
    if (-not (Test-Path -LiteralPath $debugApk -PathType Leaf)) {
        throw "The Android debug build completed, but the APK was not found at $debugApk."
    }

    Write-Stage 'Validation completed successfully'
    Write-Host "Debug APK: $debugApk" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "`nVALIDATION FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
