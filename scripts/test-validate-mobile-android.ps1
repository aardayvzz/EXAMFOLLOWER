$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# Load only function declarations: never execute the validator's build or exit.
$validatorPath = Join-Path $PSScriptRoot 'validate-mobile-android.ps1'
$parseTokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    $validatorPath, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
$functionAst = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Find-NodeToolchain'
}, $true)
Invoke-Expression $functionAst.Extent.Text

$firstDirectory = Join-Path $env:TEMP 'validator-test-first'
$secondDirectory = Join-Path $env:TEMP 'validator-test-second'
$script:nodeCandidates = @()
$script:existingPaths = @()
function Get-Command { param($Name, $CommandType, [switch]$All, $ErrorAction)
    $script:nodeCandidates | ForEach-Object { [pscustomobject]@{ Source = $_ } }
}
function Test-Path { param($LiteralPath, $PathType)
    return $script:existingPaths -contains $LiteralPath
}
function Resolve-Path { param($LiteralPath)
    return [pscustomobject]@{ Path = $LiteralPath }
}
function Assert-Throws { param([scriptblock]$Action)
    $threw = $false
    try { & $Action } catch { $threw = $true }
    if (-not $threw) { throw 'Expected discovery to fail.' }
}

$firstNode = Join-Path $firstDirectory 'node.exe'
$secondNode = Join-Path $secondDirectory 'node.exe'
$secondNpm = Join-Path $secondDirectory 'npm.cmd'
$secondNpx = Join-Path $secondDirectory 'npx.cmd'
$script:nodeCandidates = @($firstNode, $secondNode)
$script:existingPaths = @($firstNode, $secondNode, $secondNpm, $secondNpx)
$toolchain = Find-NodeToolchain
if ($toolchain.Node -isnot [string] -or $toolchain.Node -ne $secondNode -or
    $toolchain.Npm -ne $secondNpm -or $toolchain.Npx -ne $secondNpx) {
    throw 'Multiple Node installations must select one complete adjacent toolchain.'
}

$script:nodeCandidates = @('relative\node.exe', $secondNode)
$script:existingPaths += 'relative\node.exe'
if ((Find-NodeToolchain).Node -ne $secondNode) { throw 'Relative executable was selected.' }

$script:existingPaths = @($firstNode, $secondNode, $secondNpm)
Assert-Throws { Find-NodeToolchain }
$script:nodeCandidates = @()
Assert-Throws { Find-NodeToolchain }
Write-Host 'Validator discovery: 4 regression checks passed.'
