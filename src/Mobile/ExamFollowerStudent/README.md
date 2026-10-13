# ExamFollower Student Android

Öğrencinin backend'deki yaklaşan derslerini alıp Android yerel alarmı ve bildirimi olarak planlayan bare React Native uygulamasıdır. Expo, FCM, periyodik ağ işi, WebView ve kiosk/kilit özelliği içermez.

## Araçlar

- React Native 0.87.0, React 19.2.3
- Node.js 22.13 veya üzeri (doğrulanan: 24.20.0), npm 11.19.0
- JDK 17 (doğrulanan: Temurin 17.0.20.1)
- Android Gradle Plugin 9.2.1 (React Native Gradle eklentisiyle yönetilir)
- Gradle 9.4.1, Kotlin 2.2.0
- Android SDK/Build Tools 37.0.0; targetSdk 36, minSdk 24

SDK Manager'da API 37, Build-Tools 37.0.0, Platform-Tools, Command-line Tools, NDK 27.1.12297006 ve CMake 3.22.1 kurulu olmalıdır. Uygulama, Android Gradle Plugin 9.2.1'in varsayılan CMake sürümünü açıkça sabitler. ReactAndroid kaynak derlemesindeki CMake ayarı bu uygulama için sürüm seçimi değildir.

Windows kullanıcı ortamındaki `JAVA_HOME` JDK 17'ye, `ANDROID_HOME` ve `ANDROID_SDK_ROOT` aynı SDK kurulumuna işaret etmelidir. Microsoft Store uygulamalarından başlatılan işlemler AppData yollarını sanallaştırabilir; SDK konumu normal Windows dosya sisteminden erişilebilir olmalıdır. Makineye özel yolları repository'ye kaydetmeyin.

Repository kökünde doğrulama:

```powershell
powershell -ExecutionPolicy Bypass -File '.\scripts\validate-mobile-android.ps1'
```

## Geliştirme ve API adresi

Paketleri yükleyip Metro ve Android uygulamasını çalıştırın:

```powershell
npm ci
npm start
npm run android
```

Debug build varsayılan olarak emülatörün host bilgisayarına erişebilmesi için `http://10.0.2.2:5059` kullanır. Bu cleartext izni yalnızca `debug` manifest overlay'inde açıktır; release manifest'i cleartext trafiği reddeder. Fiziksel tablette bilgisayarın aynı yerel ağdaki IP adresini verin:

```powershell
cd android
.\gradlew.bat installDebug -PEXAM_FOLLOWER_API_URL=http://192.168.1.50:5059
```

Production build'inde URL varsayılan olarak boştur ve açıkça HTTPS adresi verilmelidir:

```powershell
.\gradlew.bat assembleRelease -PEXAM_FOLLOWER_API_URL=https://api.example.com
```

HTTPS sertifika doğrulaması devre dışı bırakılmaz. Backend geliştirme sunucusunun tablette erişilebilir bir arayüzde dinlediğinden emin olun.

## Oturum ve senkronizasyon

- Login: `POST /api/auth/login`; yalnızca `Student` rolü kabul edilir.
- Dersler: `GET /api/lesson-notifications/upcoming?days=30`.
- JWT ve oturum metadatası `react-native-keychain` ile Android Keystore destekli depoda tutulur. Parola saklanmaz.
- Senkronizasyon başarılı login sonrasında, geçerli oturumla uygulama açıldığında ve **Dersleri Yenile** seçildiğinde yapılır.
- API hatasında son doğrulanmış alarm snapshot'ı silinmez. 401 yanıtında güvenli oturum ve öğrenciye ait tüm alarmlar temizlenir.
- Çıkışta Keychain oturumu, occurrence snapshot'ı ve bekleyen alarmlar temizlenir.

## Bildirim ve alarm izinleri

Android 13+ `POST_NOTIFICATIONS` izni açıklama ekranından kullanıcı eylemiyle istenir. Ret uygulamayı durdurmaz ve otomatik olarak tekrar sorulmaz.

Android 12+ kesin alarm erişimi `AlarmManager.canScheduleExactAlarms()` ile denetlenir. Erişim yoksa uygulama çökmeden `setAndAllowWhileIdle` ile yaklaşık zaman fallback'ine geçer ve bunu durum kartında gösterir. Sistem ayarları ancak kullanıcı açıklamayı okuyup **Ayarları aç** eylemini seçerse açılır.

Cihaz yeniden başlatıldığında veya uygulama güncellendiğinde `BOOT_COMPLETED` / `MY_PACKAGE_REPLACED` receiver yalnızca özel uygulama deposundaki son doğrulanmış, gelecekteki occurrence snapshot'ını yeniden planlar. Receiver ağ isteği yapmaz ve JWT okumaz.

## Kontroller

```powershell
npm run typecheck
npm run lint
npm test
cd android
.\gradlew.bat testDebugUnitTest
.\gradlew.bat assembleDebug
```

## Fiziksel tablet kontrol listesi

1. Bilgisayar ve tableti aynı ağa bağlayıp `EXAM_FOLLOWER_API_URL` değerini bilgisayarın LAN IP'siyle verin.
2. Student hesabıyla giriş yapın; Android 13+ bildirim açıklamasından izni verin.
3. Yakın gelecekte başlayan test dersini senkronize edip bildirimin ders saatinde geldiğini doğrulayın.
4. Aynı feed'i iki kez yenileyip tek alarm/bildirim oluştuğunu doğrulayın.
5. Ders saatini backend kaynağında değiştirip yenileyin; eski zamanda bildirim gelmediğini ve yenisinin planlandığını doğrulayın.
6. Tableti yeniden başlatıp gelecekteki alarmın geri geldiğini doğrulayın.
7. Kesin alarm erişimini kapatıp durum kartında fallback mesajının göründüğünü ve uygulamanın çalışmaya devam ettiğini doğrulayın.
