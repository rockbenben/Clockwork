<div align="center">

<img src="../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**PC'nizin tekrarlayan işlerini otomatik pilota alın**

Oturum açınca uygulamalarınızı otomatik başlatın · zamanlı hatırlatıcılar · tek dokunuşla koca bir rutini çalıştırın

</div>

<div align="center">

[English](../README.md) · [简体中文](README.zh-CN.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · **Türkçe** · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> 365 Açık Kaynak Planı #020 · Bir Windows tepsi aracı: başlangıç başlatıcısı · hatırlatıcılar · sistem başlangıç öğeleri · eylem grupları

![Clockwork](../assets/social-card.png)

Bilgisayar başında gününüze başlarken karşılaşılan rutin işleri üstlenen küçük bir Windows tepsi aracı:

- 🚀 **Başlangıç listesi** — oturum açınca günlük uygulamalarınızı sırayla otomatik açar (adım başına yönetici hakları, gecikmeler, yalnızca-belirli-hafta-günlerinde / yalnızca-saat-N'den-önce, pencere stili, çalışıyorsa-etkinleştir, yedek yollar) ve bu arada birkaç işi de halleder (pencereleri kapat veya odakla, tuş vuruşu / metin gönder, ses düzeyini ayarla…).
- ⏰ **Hatırlatıcılar** — zamanı gelince bir hatırlatma gösterir; sesli okur; hafta gününe göre / her-N-günde / aylık tekrarlar; ya da "oturum açınca" tetiklenir. **Evet**'e tıklamak bir program çalıştırabilir, bir dosya (örneğin müzik) veya bir URL açabilir ya da bir eylem grubu çalıştırabilir.
- 🧹 **Sistem başlangıç öğeleri** — **PC'nizde otomatik başlayan her şeyi** listeler ve ihtiyacınız olmayanları kapatır (silinmez, devre dışı bırakılır — istediğiniz zaman geri açın). Tek tıkla bir öğeyi kendi başlangıç listenize "devralır".
- 🎛️ **Eylem grupları** — bir dizi eylemi yeniden kullanılabilir bir grupta toplayın (Odak / Toplantı / Kapanış / Uyku vakti…) ve tepsiden, bir **genel kısayoldan**, başlangıç listesinden ya da bir hatırlatıcıdan tek tıkla tetikleyin. Yerleşik şablonlar dahildir.

Kurulum yok, tek klasörde tamamen taşınabilir, her şey fareyle yapılandırılabilir; koyu arayüz, yüksek DPI uyumlu.

> 📖 **Tam kılavuz:** [English](USAGE.md) · [中文](USAGE.zh-CN.md)

## Gereksinimler

- Windows 10 / 11 (x64)
- Kurulacak bir şey yok: .NET çalışma zamanı gömülü, kendi kendine yeten tek dosyalık bir `Clockwork.exe`.

## Başlarken

1. En son `Clockwork-<sürüm>.zip`'i [Releases](https://github.com/rockbenben/Clockwork/releases) sayfasından indirin ve arşivden çıkarın — içinde tek bir `Clockwork.exe` var; onu herhangi bir klasöre bırakın (taşınabilir — istediğiniz yere koyun). Kendiniz derlemek için aşağıdaki **Geliştiriciler için** bölümüne bakın.
2. Ayarlar penceresini açmak için **`Clockwork.exe`**'ye çift tıklayın.
   - **İlk çalıştırmada** başlangıç listesine ve hatırlatıcılara kendinize uyarlayabileceğiniz birkaç **örnek** yükler — hepsi başlangıçta işaretsizdir, yani siz işaretlemeden hiçbir şey çalışmaz. Ayarlarınız exe'nin yanındaki `clockwork.settings.json` içinde durur — yalnızca yerel, asla depoya işlenmez.
3. Her açılışta çalıştırmak için: **Ayarlar** sekmesinde **Oturum açınca başlat**'a tıklayın (yönetici haklarıyla bir zamanlanmış görev kaydeder, böylece açılışta yığınla UAC istemi olmaz).

> Sessizce tepside durur. Pencereyi açmak için tepsi simgesine çift tıklayın; pencerenin kapat düğmesi onu yalnızca tepsiye gizler. Gerçekten çıkmak için tepsinin sağ tık menüsündeki **Çıkış**'ı kullanın.

## Ekran görüntüsü

![Screenshot](../assets/screenshot.png)

## Beş sekme

### Başlangıç listesi

Oturum açınca yukarıdan aşağıya çalıştırılan **sıralı bir adımlar listesi**. Bir tür seçmek için **Ekle ▾**'ye tıklayın; serbestçe ekleyin/kaldırın/yeniden sıralayın; her adım etkinleştirilip/devre dışı bırakılabilir, ona bir **adım sonrası gecikme**, bir **tekrar sayısı** (N kez döngüye al) ve koşullar (**yalnızca belirli hafta günlerinde / yalnızca saat N'den önce**) verilebilir. Adım türleri:

- **Program başlat** — hedef (dosya seçmek için **Gözat…**) / bağımsız değişkenler / çalışma dizini (boş bırak = hedefin klasörü) / yönetici. Hedef bir `.exe`, belge, kısayol veya URL olabilir; bir `.ps1` PowerShell üzerinden çalışır. Gelişmiş: **pencere stili** (simge durumunda / tam ekran / gizli), **zaten çalışıyorsa etkinleştir** (yeniden başlatmak yerine öne getir; işlem adı **Seç…** ile), **yedek yollar** (her satıra bir tam yol; var olan ilki kullanılır — kurulum yolları makineler arasında farklıysa işe yarar).
- **Tuş gönder** — örneğin Win+D, Alt+K, Ctrl+Enter, F5 (bir kısayolu basarak kaydetmek için **Yakala**).
- **Metin gönder** — odaktaki pencereye (ya da **Seç…** ile seçilen bir **hedef işleme**) bir dize yazar.
- **Ses** — sessize al / sesi aç / düzey ayarla.
- **Pencere eylemi** — işlem adına göre (**Seç…**, aranabilir): kapat / simge durumuna küçült / tam ekran yap / öne getir / öne-getir-ve-tuş-gönder; yavaş uygulamalar için **pencere görünene dek N saniyeye kadar bekle**.
- **Sistem komutu** — masaüstünü göster / kilitle / monitörü kapat / geri dönüşüm kutusunu boşalt / panoyu temizle / Ayarlar'ı aç / Görev Yöneticisi / ekran görüntüsü / uyku / hazırda beklet / oturumu kapat / yeniden başlat / kapat (son üçü önce onay ister).
- **Gecikme** — sonraki adımdan önce yalnızca N saniye bekle.
- **Eylem grubu** — tanımlı bir eylem grubunu çalıştır; tüm grubu döngüye almak için bir tekrar sayısı ayarla.

> **Başlangıç gecikmesi** (Ayarlar sekmesi, yalnızca açılışta): oturum açtıktan sonra sabit bir saniye sayısı bekleyerek "oturum açma fırtınası" (her otomatik başlangıçtan gelen disk/CPU çekişmesi) geçene dek liste çalışmasın; elle yeniden çalıştırma bundan etkilenmez. Her şey çok erken başlıyorsa artırın (0–600 sn).

> **İstediğiniz an durdurun** — tepsi → **Çalışan eylemleri durdur** ya da genel **panik kısayolu** (Ayarlar sekmesinde ayarlanır; varsayılan `Ctrl+Alt+Q`). Çalışmakta olan, geçerli eylemden sonra durur; uzun beklemeler (başlangıç gecikmesi, bir pencereyi bekleme) hemen kesilir.

### Hatırlatıcılar

Bir **saat** ayarlayın (ya da **oturum açınca**'ya geçin), bir **yineleme** (hafta günleri / her-N-günde / aylık) ve **metin**; isteğe bağlı olarak sesli okutun. **Evet'te** eylemi olan (program çalıştır / dosya aç / URL / eylem grubu çalıştır) hatırlatıcılar, **Ertele** düğmeli (varsayılan 10 dk, ▾ menüsü 5–60 dk) bir **Evet / Hayır** iletişim kutusu açar; geri kalanı köşeye bir **hatırlatma kartı** olarak kayar (yapılandırılan saniyeden sonra otomatik kapanır, **0 = siz kapatana dek kalır**). Ayrıca bir **sessiz eylem grubu** da ayarlayabilirsiniz — zamanı gelince açılır pencere olmadan bir grup çalıştırır.

Gelişmiş: **otomatik kapanma**, **ısrarlı tekrar** (bir son teslim tarihine dek her N dakikada yeniden açılır), **tetik sonrası gecikme + rastgele sapma**, **tolerans** (kısa bir kapanma/uyku nedeniyle kaçan bir tetiklemeyi yakalar), **kaçırıldıysa telafi et** (hazırda bekleme/kapanma onu atladıysa bir kez daha tetikler) ve her-N-günde için bir **çıpa tarihi** (**Tarih seç**). "Bugün tetiklendi" ve "şu ana dek ertelendi" yeniden başlatmalardan sağ çıkar (`clockwork.state.json`), böylece bir erteleme yeniden başlatma boyunca taşınır ve hiçbir şey iki kez tetiklenmez.

Odaklanmanız ya da bir toplantıya girmeniz mi gerekiyor? Tepsi **Hatırlatıcıları 1 / 2 / 4 saat duraklat** (Rahatsız Etmeyin) seçeneği sunar: her şey (sessiz gruplar dahil) bastırılır ve süre dolunca otomatik olarak devam eder.

### Sistem başlangıç öğeleri

**Otomatik başlayan her şeyi** listeler (kayıt defteri Run anahtarları, Başlangıç klasörleri, zamanlanmış görevler). Bir öğeyi kapatmak için **Etkinleştir**'in işaretini kaldırın — **silinmez, devre dışı bırakılır; geri yüklemek için yeniden işaretleyin** (hemen etkili olur). **Yönetici gerektirir** olarak işaretli öğeler yükseltilmiş olarak yeniden başlatmayı ister. Sistem / ilke / tek seferlik öğeler (Grup İlkesi Run, RunOnce, Winlogon, Active Setup) dokunulamaz ve **varsayılan olarak gizlidir** — bunları görmek için **Sistem / salt okunur öğeleri göster**'i işaretleyin (soluk gösterilir). Bir satıra sağ tıklayarak **Başlangıç listesine al** (öğeyi Clockwork'e devreder; yalnızca kayıt defteri Run anahtarları ve Başlangıç klasörü öğeleri) ya da **Sistemden sil** (kaydı kalıcı olarak kaldırır — önce sorar ve geri alınamaz; geri alınabilir seçenek işareti kaldırmaktır) seçeneğine ulaşabilirsiniz. Üstteki bir **filtre** ad / komuta göre arar; kısaltılmış bir komutu tam okumak için üzerine gelin.

### Eylem grupları

Eylemleri yeniden kullanılabilir bir grupta toplayın. **Ekle ▾**, bir **yerleşik şablondan** (Odak / Toplantı / Kapanış / Uyku vakti / Uzaklaşma / Ekran görüntüsü) başlatır — işlem adlarını ayarlayıp kaydedin. Bir grup **yalnızca eylemleri tanımlar**; onu dört şekilde tetikleyin: tepsiden (**Çalıştır: <grup>**), bir **genel kısayolla**, başlangıç listesinde bir **eylem grubu adımı** olarak (açılışta) ya da bir hatırlatıcıdan (**Evet'te / sessiz grup**). Bir grubun aynı anda yalnızca tek bir kopyası çalışır; bir **mesaj** adımı bir onay kapısı işlevi görebilir (**Hayır** yanıtı geri kalanı iptal eder).

> **Genel kısayol** — grup düzenleyicide kısayol kutusuna tıklayın ve bir kısayola (ör. `Ctrl+Alt+F`) basarak o grubu menüye gerek kalmadan her yerden çalıştırın. Esc iptal eder, Delete temizler. Devre dışı gruplar kombinasyonlarını serbest bırakır; sistemce ayrılmış kombinasyonlar (Alt+F4, Ctrl+Shift+Esc…) ve başka bir grup ya da panik kısayolu tarafından zaten kullanılan kombinasyonlar bir bildirimle reddedilir.

### Ayarlar

**Başlangıç gecikmesi** (0–600 sn, yalnızca açılışta), **tepsiye küçültülmüş başlat**, **panik kısayolu** (kutuya tıklayıp kısayolunuza basın; Esc iptal eder, Delete temizler; varsayılan `Ctrl+Alt+Q`) ve **arayüz dili** (Basitleştirilmiş Çince, İngilizce, 日本語 ve 15 tane daha — toplam 18; değiştirmek uygulamak için uygulamayı yeniden başlatır).

**Yapılandırmayı dışa aktar / Yapılandırmayı içe aktar** — tüm kurulumunuzu başka bir bilgisayara taşıyın ya da bir yedek tutun. Dışa aktarma, `clockwork.settings.json` dosyasının bir kopyasını istediğiniz yere yazar; içe aktarma **her şeyin** (başlangıç listesi / hatırlatıcılar / eylem grupları / ayarlar) yerine geçer, bu yüzden önce onay ister, geçerli yapılandırmayı `clockwork.settings.json.bak` olarak yedekler ve uygulamak için uygulamayı yeniden başlatır.

## İpuçları

- **Bir satırı düzenlemek için çift tıklayın.** Yolları / işlemleri / kısayolları / tarihleri doldururken elle yazmanız gerekmez: **Gözat…**, **Seç…** (aranabilir işlem seçici), **Yakala** ve **Tarih seç**.
- **Çoğalt** (Hatırlatıcılar / Eylem grupları sekmeleri), seçili satırın bir kopyasını hemen altına ekler — neredeyse aynı olan bir kaydı sıfırdan kurmaktan hızlıdır; çoğaltılan bir grup "… (kopya)" olarak adlandırılır.
- **Silme her yerde önce onay ister** — liste satırları, grup düzenleyicideki adımlar ve sistem başlangıç öğeleri.
- `Clockwork.exe`'ye çift tıklamak yalnızca ayarları açar — başlangıç listesini hemen **çalıştırmaz**; bunun için tepsinin **Başlangıç listesini yeniden çalıştır**'ını kullanın.
- **Normal şekilde başlatın** (çift tıklama / tepsi / zamanlanmış görev). Bazı sandbox / düşük ayrıcalıklı başlatıcılar düşük seviyeli çağrıları engeller, bu yüzden tuş gönderme / pencere eylemleri / çalışıyorsa-etkinleştir / işleme-metin-gönderme / ses çalışmayabilir (net bir uyarı alırsınız; düz "program başlat" etkilenmez).
- Yapılandırmanız `clockwork.settings.json`'dır (yalnızca yerel). Örneğe sıfırlamak için silin. Hatırlatıcı durumu `clockwork.state.json`'dır (o da yerel; silmesi güvenli).
- Bir `.ahk` adımı eklemek için AutoHotkey'in kurulu olması gerekir. Genel kısayollar / metin genişletme kapsam dışıdır — o, AutoHotkey'in güçlü yanıdır.

## Geliştiriciler için

C#/.NET WPF; kaynak `app/` içinde (.NET 10 SDK gerekir). Katmanlar: `Core/` saf mantık · `Native/` Win32 birlikte çalışma · `Engine/` yürütme · `ViewModels/` + `Views/` arayüz · `I18n/` + `Resources/` yerelleştirme (nötr = Çince kaynak, her dil için bir `Strings.<code>.resx` uydusu).

- Testleri çalıştır (xUnit):
  ```powershell
  dotnet test app.Tests/Clockwork.Tests.csproj
  ```
- Kendi kendine yeten tek dosyalık exe'yi derle (tek dosya / kendine yeten / sıkıştırma csproj'da ayarlıdır):
  ```powershell
  dotnet publish app/Clockwork.csproj -c Release -r win-x64
  ```
  Çıktı: `app/bin/Release/net10.0-windows/win-x64/publish/Clockwork.exe`.
- **CI / sürümler** (GitHub Actions): push / PR'ler bir Windows çalıştırıcısında derler ve tüm testleri çalıştırır; bir `v*` etiketi (örneğin `v2.0.0`) göndermek derler, dosya sürümünü etiketten damgalar, bir GitHub Release oluşturur ve `Clockwork-<etiket>.zip`'i (içinde `Clockwork.exe` bulunur) ekler.

## 365 Açık Kaynak Planı hakkında

Bu, [365 Açık Kaynak Planı](https://github.com/rockbenben/365opensource)'nın 20 numaralı projesidir — bir kişi + yapay zeka, bir yılda 300'ün üzerinde açık kaynak proje. [İstek gönder →](https://365.aishort.top/)

## Lisans

[MIT](../LICENSE) © rockbenben
