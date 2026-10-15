# Android tablet bildirim doğrulaması ve kilit tasarımı

İnceleme tarihi: 15 Ekim 2026. Referans: `e6d6e72a352a79d99ee11f14912cf6f87c3094bf`.

Durum: tasarım önerisi; kilit yaklaşımı henüz seçilmedi. Bu çalışma kilit kodu, provisioning, cihaz sıfırlama veya Zoom entegrasyonu içermez.

## Fiziksel tablet doğrulama durumu

Başlangıçta `main` dalı ve temiz çalışma ağacı doğrulandı; referans commit HEAD idi ve branch geçmişinde bulundu. Repository kapsamında `AGENTS.md` bulunmadı. Mobil README ve mevcut Kotlin/TypeScript bileşenleri incelendi. Önceden doğrulanmış SDK'nın adb 37.0.1 aracı, yetkili Windows ortamında çalıştırıldı. Makineye özel SDK yolu bu belgeye eklenmedi.

`adb devices -l` yalnızca `List of devices attached` başlığını döndürdü; bağlı veya yetkilendirme bekleyen cihaz satırı yoktu. Uygulama kurulmadı, kullanıcı verileri değiştirilmedi, backend/test hesabı oluşturulmadı. Önceki otomatik test sonuçları fiziksel cihaz kanıtı yerine kullanılmadı.

| Gerçek tablet kontrolü | Durum / kaydedilecek kanıt |
| --- | --- |
| Öğrenci girişi ve yaklaşan dersler | Bekliyor; ayrı test hesabıyla feed ve UI karşılaştırması |
| Bildirim izni kabul / ret | Bekliyor; iki senaryoda uygulamanın devam etmesi ve durum ekranı |
| Exact alarm erişimi | Bekliyor; sistem ayarı ve uygulama özeti karşılaştırması |
| Yakın zamandaki ders bildirimi | Bekliyor; planlanan zaman, gözlenen zaman ve fark (saniye) |
| Aynı occurrence'ı tekrar senkronize etme | Bekliyor; tek bekleyen alarm ve tek bildirim |
| Başlangıç saatini değiştirme | Bekliyor; eski alarmın yokluğu, yeni zamanın gözlenmesi |
| Başarılı snapshot'tan kaldırma | Bekliyor; kaldırılan occurrence'ın bekleyen alarmının yokluğu |
| API erişilemiyorken yenileme | Bekliyor; önceki geçerli alarmların korunması |
| Uygulama arka plandayken bildirim | Bekliyor; uygulamayı zorla durdurmadan arka plan senaryosu |
| Tableti yeniden başlatma | Bekliyor; cihaz açılışı/kilit açılması sonrasında geçerli alarmlar |
| Çıkış | Bekliyor; güvenli oturum, snapshot ve bekleyen alarmların temizliği |

Bildirim gecikmesi için henüz ölçüm yoktur; sıfır gecikme veya kesin zaman garantisi verilmez. Fallback, Doze/pil kısıtları ve OEM davranışı ayrıca kaydedilmelidir. [AlarmManager zamanlama sınırlamaları](https://developer.android.com/reference/android/app/AlarmManager#setAndAllowWhileIdle(int,long,android.app.PendingIntent)).

Zorla durdurma, normal arka plan kullanımından ayrıdır. Android 15 stopped-state davranışı PendingIntent'leri iptal eder; uygulama kullanıcı etkileşimiyle tekrar etkinleşmeden alarm güvenilirliği varsayılamaz. Sonrasında yeniden planlama da test edilmelidir. [Android 15 stopped state](https://developer.android.com/about/versions/15/behavior-changes-all#stopped-state).

Bağlantı checkpoint'i için kullanıcıdan gerekenler:

1. Tablette Geliştirici seçenekleri ve USB hata ayıklamasını açmak.
2. Veri aktarabilen USB kablosuyla bilgisayara bağlamak.
3. Tablet ekranındaki bu bilgisayar için USB hata ayıklama yetkilendirmesini onaylamak.

Bağlantıdan sonra birden fazla cihaz varsa hedef seçilmeden kurulum yapılmamalıdır. Mevcut APK veri silmeden güncellenmelidir; imza uyuşmazlığında kaldırma/veri kaybı için önce kullanıcı bilgilendirilmelidir. Varsayılan debug URL'si emülatör içindir; fiziksel tablet için USB üzerinden `adb reverse` ve localhost API yapılandırması tercih edilerek backend loopback'te tutulabilir. Reverse tek başına APK'ya gömülü API URL'sini değiştirmez. Debug APK için Metro bağlantısı da ayrıca sağlanmalıdır.

Testler yalnızca desteklenen backend akışları ve ayrı test verileriyle yapılmalıdır. Gerçek Kurspro programı, doğrudan SQL veya production endpoint değişiklikleri kullanılmaz; kullanıcı parolası/JWT istenmez ve hassas çıktı kaydedilmez.

## İncelenen mevcut yapı

Mobil kök: `src/Mobile/ExamFollowerStudent`. React Native 0.87.0, Kotlin 2.2.0; minSdk 24, targetSdk 36.

| Bileşen | Bugünkü davranış / tasarım etkisi |
| --- | --- |
| `App.tsx`, `src/native/lessonNotifications.ts` | Öğrenci oturumu, foreground senkronizasyon ve bildirim modülü köprüsü |
| `MainActivity.kt` | ReactActivity; kilit API'si veya kilit lifecycle takibi yok |
| `notifications/AppNativePackage.kt` | AppConfig ve LessonNotifications modüllerini kaydeder; kilit modülü yok |
| `LessonAlarmScheduler.kt` | AlarmManager; exact erişimi yoksa yaklaşık zaman fallback'i |
| `LessonAlarmReceiver.kt` | Bildirim üretir; content PendingIntent doğrudan MainActivity'yi açar, receiver activity açmaz |
| `BootReceiver.kt` | Snapshot'taki geçerli gelecekteki alarmları yeniden kurar; ağ/JWT erişimi yok |
| `AndroidManifest.xml` | Bildirim, alarm ve boot izinleri var; DPC/admin receiver veya lockTaskMode yapılandırması yok |

Backend kaynakları: `Application/RecurringLessons/Contracts/LessonNotificationResponse.cs` ve `Controllers/LessonNotificationsController.cs` (backend proje köküne göre). `GET /api/lesson-notifications/upcoming?days=30` yanıtı `generatedAtUtc`, `validUntilUtc`, `items`; occurrence alanları `occurrenceId`, `scheduleId`, `title`, `startsAtUtc`, `endsAtUtc`, `reminderAtUtc`, `lockRequired`, `source`. Join URL bu kontratta yoktur. Alanların yorumlanması veya bu rapor backend kontratını değiştirmez.

## İki farklı kilit yaklaşımı

| Konu | Screen Pinning | Yönetilen Lock Task |
| --- | --- | --- |
| Başlatma | Allowlist'te olmayan normal activity'nin talebi sistem sabitleme onayına gider | DPC'nin izin verdiği paket uygun akışla Lock Task'a girer |
| Kullanıcı kontrolü | Öğrenci Android'in çıkış hareketi/tuşlarıyla sabitlemeyi bırakabilir | Normal kullanıcı uygulama kümesinin dışına çıkamaz; yönetim politikası belirleyicidir |
| Güvence | Dikkat desteği; uygulamadan ayrılmayı kesin engellemez | Yönetilen cihaz ve yapılandırılmış politika sınırlarında kısıtlama |
| İlk kurulum | Device Owner gerektirmez; cihaz ayarı/OEM akışı teyit edilir | Uygun DPC yönetim yetkisi ve allowlist gerekir |

Normal `startLockTask()` talebinin onay diyaloğu davranışı [manifest lockTaskMode başvurusunda](https://developer.android.com/reference/android/R.styleable#AndroidManifestActivity_lockTaskMode) tanımlıdır. Screen Pinning'den çıkışta cihazın ekran kilidi istenebilmesi onu uygulamanın yönettiği bir veli PIN'i yapmaz; sabitlemeyi kalıcı kilit saymamak gerekir. [Screen Pinning ve Lock Task ayrımı](https://developer.android.com/work/dpc/dedicated-devices/lock-task-mode#overview).

### Yönetim ve provisioning

Tam cihaz kısıtlaması için hedef mimari fully managed tablet üzerinde Device Owner DPC'dir. Öğrenci uygulaması allowlist'te olabilir; DPC ile aynı paket olması zorunlu değildir. Allowlist değiştirmek normal runtime izinlerinden farklı yetkiler gerektirir. API bazı uygun profile-owner/yönetim izni durumlarını da destekler; sıradan bir iş profili kurulmasını tüm kişisel tablet üzerinde denetim olarak yorumlamayın. [DevicePolicyManager.setLockTaskPackages](https://developer.android.com/reference/android/app/admin/DevicePolicyManager#setLockTaskPackages(android.content.ComponentName,java.lang.String%5B%5D)).

Üretim provisioning akışları yeni/fabrika ayarlarına dönmüş cihazın kurulumunda uygulanır. Kullanımdaki tabletin sahipliği, mevcut yönetimi, hesapları, yedekleri ve OEM/Android desteği bilinmediğinden veri koruyan bir Device Owner dönüşümü vaat edilemez; sıfırlama gerekebilir. Ayrı onay ve kurtarma planı olmadan provisioning veya reset yapılmaz. [Android Management provisioning](https://developers.google.com/android/management/provision-device).

Gelecekteki tasarım için önerilen kabul koşulları: yetkili yöneticinin çevrimdışı çıkış yolu; uygulama/DPC çökmesi, ağ yokluğu, yanlış allowlist, güncelleme ve yeniden başlatma testleri; keyguard açıkken güvenli davranış; yönetici erişimini kaybetmeden geri alınabilir politika. Normal uygulama kendi başlatmadığı kilidi kaldırabildiğini varsaymamalıdır. Başlatan activity'nin çıkış yetkisi [Activity.stopLockTask başvurusundan](https://developer.android.com/reference/android/app/Activity#stopLockTask()) doğrulanır; DPC kurtarma yolu ayrıca tasarlanır. Sıfırlama rutin çıkış yöntemi değildir.

Yönetilen kilitte bildirim alanının gizlenmesi ders bildirimi akışını da etkileyebilir. DPC'nin `setLockTaskFeatures()` politikası bildirim, Home ve sistem UI gereksinimleriyle birlikte tasarlanmalıdır; bildirim izni verilmiş olması bildirimin kilit modunda kullanıcıya görünmesini garanti etmez. UI politikasının Zoom'a geçiş ve kurtarma üzerindeki etkisi gerçek cihazda ayrı test edilir. [Lock Task UI politikası](https://developer.android.com/reference/android/app/admin/DevicePolicyManager#setLockTaskFeatures(android.content.ComponentName,int)).

## Ders başlangıcı ve arka plan sınırları

Normal uygulamada receiver'ın alarm alması foreground activity veya cihaz yönetim yetkisi sağlamaz. Arka plandan UI başlatma Android 10+ kısıtlarına tabidir; kesin alarm ya da bildirim izni bunları topluca kaldırmaz. Foreground service de tek başına foreground activity değildir. Mevcut receiver'ın bildirim gösteren akışı korunmalıdır. Full-screen intent/overlay bir ders kilidi kestirmesi olarak önerilmez. [Activity güvenliği ve BAL](https://developer.android.com/guide/components/activities/secure-bal).

Önerilen kullanıcı etkileşimli akış: ders alarmı → bildirim → kullanıcının bildirime dokunması → MainActivity'nin resume olması → oturum, geçerli occurrence ve keyguard kontrolü → kilit yeteneğinin yeniden sorgulanması → seçilen moda ilişkin açıklama/talep → sistem durumunun tekrar okunması. Kullanıcı dokunana kadar ders bitmiş, oturum kapanmış veya snapshot değişmişse eski bildirime dayanarak kilit istenmez. Bildirimin intent verileri tek başına yetkili ders kaydı sayılmaz. Bu akış bir sonraki geliştirme önerisidir; mevcut content intent occurrence kimliğini activity'ye taşımıyor.

Kullanıcı etkileşimsiz giriş ancak yönetilen mimaride, activity başlatan DPC'nin geçerli BAL istisnası ve hedef paketin Lock Task izni birlikte doğrulandığında planlanabilir. AOSP'deki Device Owner/uygun affiliated profile owner istisnası çağıran UID içindir; DPC'nin allowlist'e aldığı sıradan öğrenci uygulaması bu istisnayı miras almaz. [AOSP BackgroundActivityStartController](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/services/core/java/com/android/server/wm/BackgroundActivityStartController.java).

Android 9+ DPC başka uygulamayı Lock Task seçeneğiyle başlatabilir. Allowlist, activity başlatma yetkisi, açık/keyguard durumu ve OS/OEM doğrulaması birlikte gerekir. Bir alarmın bulunması otomatik kilit garantisi değildir. [DPC ile başlatma](https://developer.android.com/work/dpc/dedicated-devices/lock-task-mode#start_lock_task_mode).

Bildirim izni kullanıcıya bildirim gösterme, exact alarm erişimi zamanlama, DPC yetkisi ise cihaz politikası içindir; birbirlerinin yerine geçmez. Hedef SDK 36 nedeniyle PendingIntent/BAL opt-in kuralları da sonraki implementasyonda ayrı incelenmelidir; mevcut bildirime dokunma davranışı cihazda henüz denenmedi.

## Zoom ile birlikte çalışma

Screen Pinning'de öğrenci uygulamasını sabitlemek ayrı bir Zoom task'ına sorunsuz geçiş sağlayacağı anlamına gelmez; sabitlemeden çıkma gerekebilir. Kesin sonuç activity/task davranışı ve gerçek cihazda denenmelidir. [Activity ve task kilitleme davranışı](https://developer.android.com/reference/android/app/Activity#startLockTask()).

Yönetilen modelde öğrenci uygulaması ve doğrulanmış Zoom paketi allowlist'e alınırsa izinli uygulama kümesi genişler; amaç artık tek uygulamada kalmak değildir. Bu, Zoom'un bütün ekranlarını veya toplantılarını belirli bir dersle sınırlandırmaz. Paket, imza/dağıtım, kurulu sürüm, uygulamalar arası geçiş ve güvenli geri dönüş ayrıca doğrulanmalıdır. Lock Task'ta task/activity ayrımı bulunduğundan allowlist tek başına her olası navigasyonu sınırlayan ders politikası sayılmaz. [Task ve allowlist davranışı](https://developer.android.com/reference/android/R.styleable#AndroidManifestActivity_lockTaskMode).

Mevcut bildirim feed'i Zoom join URL garantilemez. `lockRequired` yalnızca ders metadatasıdır; DPC kurmaz, sistem onayı yerine geçmez ve cihazı kilitlemez. Otomatik Zoom açma veya yeni URL/backend kontratı bu aşamada tasarıma eklenmedi.

## Gelecek Native Module kontratı — öneri, implementasyon değil

Önerilen ayrı `LockTask` modülü `AppNativePackage` üzerinden kayıt olabilir. Aşağıdaki adlar mevcut API değildir; yaklaşım seçildikten sonra kesinleştirilecek yerel kontrattır. Backend endpoint'i tanımlanmaz.

Ortak response alanları: `requestId` (mutasyon yanıtlarında; sorgularda null), `observedAtEpochMillis`, `systemState` (`NONE | PINNED | LOCKED | UNKNOWN`), `activityResumed`, `keyguardLocked`, `packageLockTaskPermitted`. `UNKNOWN` başarılı/kilitli kabul edilmez. Snapshot yalnızca gözlem anını temsil eder. `stopOwnTaskSupported` doğrulanamayan sahiplik/yetkide null döner; çıkış yapılabilir sayılmaz.

| İşlem | Önerilen request | Önerilen response / sonuç |
| --- | --- | --- |
| `getCapabilities` | Yok | `apiLevel`, ortak alanlar, `appIsDeviceOwner`, `appIsProfileOwner`, `screenPinningRequestSupported`, `managedEntryPermitted`, `stopOwnTaskSupported`, `blockers[]`. Başka DPC'nin yönetimi için öğrenci uygulamasının owner olması gerekmez; paket izni ayrıca okunur. |
| `requestStart` | `requestId`, `mode` (`SCREEN_PINNING` veya `MANAGED`), `occurrenceId` | `outcome` (`ALREADY_ACTIVE`, `REQUESTED`, `CONFIRMATION_PENDING`) ve ortak alanlar. `MANAGED` izni yoksa Screen Pinning'e sessiz düşüş yok. Başlatma talebi aktif kilit kanıtı değildir. |
| `getState` | Yok | Ortak alanlar ve `taskOwnership` (`STARTED_HERE`, `NOT_STARTED_HERE`, `UNKNOWN`). Süreç sonrası yerel kayıt tek başına ownership kanıtı değildir. |
| `requestStop` | `requestId`, `reason` (`USER_REQUEST`, `LESSON_ENDED`, `SIGN_OUT`) | `outcome` (`ALREADY_INACTIVE`, `STOP_REQUESTED`), ortak alanlar. Reason yetkilendirme değildir; çıkış politikası ve activity/başlatan ilişkisi ayrıca doğrulanır. Başka task'a/DPC'ye ait kilit için başarı uydurulmaz. |

`systemState`, API 23+ `ActivityManager.getLockTaskModeState()` değerlerinden okunur; minSdk 24 bunu destekler. Bu bilgi sistem kilit modunu bildirir, tek başına hangi occurrence'ın/uygulamanın kilidi başlattığını kanıtlamaz. `isLockTaskPermitted()` ise yetenektir, mevcut kilit durumu değildir. [ActivityManager durum API'si](https://developer.android.com/reference/android/app/ActivityManager#getLockTaskModeState()), [paket yeteneği API'si](https://developer.android.com/reference/kotlin/android/app/admin/DevicePolicyManager#isLockTaskPermitted(kotlin.String)).

Önerilen uygulama kuralları: mutasyonlar UI thread'de ve resumed activity üzerinden yürütülür; önce/sonra ve `onResume` sırasında gerçek durum tekrar okunur. DPC lifecycle callback'leri varsa yeni sorguyu tetikler. Yerel boolean, Promise resolve olması, bildirim veya ders saati `LOCKED` kanıtı sayılmaz. Aynı `requestId` ile eşzamanlı talepler tekilleştirilir; tekrarlanan çağrı durum tekrar okunarak idempotent ele alınır. Cihaz/oturum/occurrence kontrolleri istemciden gelen kimliğe güvenmeden yapılır.

`ALREADY_ACTIVE` yalnızca gözlenen durum istenen modla eşleşiyorsa döner. `PINNED` ile `LOCKED` eşdeğer değildir; farklı moda geçiş sessizce yapılmaz. İstek başka task'ın kilidine denk geliyorsa sahiplik de doğrulanmadan bu uygulamanın dersi kilitlendi denmez.

Önerilen hata/result ayrımı:

| Hata | Anlam / uygulama tepkisi |
| --- | --- |
| `INVALID_REQUEST`, `INVALID_LESSON_CONTEXT` | Boş kimlik, desteklenmeyen mod veya artık geçersiz ders; sistem çağrısı yok |
| `NO_FOREGROUND_ACTIVITY`, `DEVICE_LOCKED` | UI uygun değil; otomatik tekrar/arka plandan UI yok |
| `NOT_ALLOWLISTED`, `POLICY_DENIED` | Yönetilen giriş yetkisi yok veya politika engeli; mod değiştirme yok |
| `REQUEST_IN_PROGRESS` | Eşzamanlı istek; yeni sistem diyaloğu açılmaz |
| `MODE_CONFLICT` | Gözlenen aktif mod istenenden farklı; örtülü mod geçişi yok |
| `STATE_UNAVAILABLE`, `SYSTEM_CALL_FAILED` | Durum okunamadı/sistem çağrısı başarısız; başarı gösterilmez |
| `CONFIRMATION_NOT_OBSERVED` | Sistem onayı süresince hedef durum gözlenmedi; kesin kullanıcı reddi gibi sunulmaz |
| `STOP_NOT_PERMITTED` | Çıkış yetkisi/başlatan ilişkisi yok; tanımlı yönetici kurtarma yoluna yönlendirme |

`CONFIRMATION_PENDING` ve `REQUESTED` başarılı kilit sonuçları değildir. İptal ile timeout ayırt edilemiyorsa bilinmiyor olarak korunur; öğrenciye yeniden tekrar onay diyaloğu açılmaz. Gelecekteki event (`onLockTaskStateChanged`) yalnızca yeni Android sorgusuyla doğrulanan state snapshot'ını taşır. Loglara JWT, parola, ders başlığı, öğrenci bilgisi veya Zoom URL eklenmez.

Veli PIN'i, uzaktan kilit açma, backend'e kilit durum bildirimi ve güvenilir DPC kontrol kanalı ayrı gelecek görevleridir. Bu işlevler için mevcut olmayan endpoint veya yerel PIN yetkisi varsayılmadı. Bunlar tasarlanmadan yönetilen çıkışın güvenli olduğu iddia edilemez.

## Karar ve sonraki kabul kapısı

Kişisel/kullanımdaki tablette kullanıcı kontrollü dikkat desteği isteniyorsa Screen Pinning; yönetilen tablet filosunda uygulama kümesi kısıtlaması isteniyorsa provisioning ve kurtarma maliyetleri kabul edilmiş Lock Task değerlendirilir. Bu bir seçim değildir; Zoom geçişi ve yönetim sahipliği netleşmeden implementasyon başlamaz.

İki seçenekte de sonraki görev gerçek state, reddedilen onay, uygulamadan çıkış, yeniden başlatma, süreç ölümü, politika değişimi, Zoom geçişi ve yetkili kurtarma testlerini ayrı kabul kriterleri yapmalıdır. Mevcut fiziksel bildirim kontrolleri hâlâ bekliyor. Bu belge için uygulama kodu değişmedi; yeni bir test/build başarısı veya fiziksel bildirim düzeltmesi iddia edilmedi.

Bildirim doğrulama durumu ve Android kilit tasarımı raporlandı. Screen Pinning veya yönetilen Lock Task yaklaşımı seçilmeden kilit implementasyonuna geçilmedi.
