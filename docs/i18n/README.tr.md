<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**PC'nizin tekrarlayan işlerini otomatik pilota alın**

Oturum açınca uygulamalarınızı otomatik başlatın · zamanlı hatırlatıcılar · tek dokunuşla koca bir rutini çalıştırın

**[⬇ Windows için indir](https://github.com/rockbenben/Clockwork/releases/latest)** — taşınabilir, kurulum gerektirmez

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../../LICENSE) [![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-3466b2)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · **Türkçe** · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

![Clockwork'ün başlangıç listesi — her biri kendi türü, gecikmesi ve koşullarıyla sıralı oturum açma adımları](../../assets/screenshot.png)

## Neler yapar

- 🚀 **Başlangıç listesi** — oturum açınca günlük uygulamalarınızı sırayla açar; her adımda gecikme, hafta günü koşulu ve pencere stili verilebilir, bu arada pencere kapatır, öne getirir veya sesi kapatır. Adımlar makinenin durumuna da bağlanabilir: yalnızca bir uygulama çalışırken (ya da çalışmıyorken), yalnızca prizdeyken veya yalnızca pildeyken, yalnızca bir dosya ya da klasör varken.
- ⏰ **Zamanlanmış görevler** — zamanı gelince bir hatırlatma (isterseniz sesli okunur) ya da sessizce çalışan bir eylem grubu. **Evet**'e tıklamak bir program çalıştırabilir, bir dosya veya URL açabilir ya da bir grubu tetikleyebilir. Ya da saat yerine bir olay tetiklesin — kilit açıldığında, kilitlendiğinde, uykudan uyanınca, N dakika boşta kalınca, şarj takılıp çıkarıldığında ya da pil azaldığında. Sadece bir kez mi gerekiyor? Tepside bir **hızlı hatırlatma** var — 5 ila 60 dakika, bir kez çalar ve kendini siler. Başka tetikleyiciler donanımı izler — ekran değişimi, ağın geri gelmesi ya da kopması, takılan bir USB sürücü — biri de sizi izler: aralıksız N dakika çalıştıktan sonra kalkmanızı hatırlatır.
- 🎛️ **Eylem grupları** — bir rutini paketleyin (Odak / Toplantı / Kapanış / Uyku vakti…) ve tepsiden, bir **genel kısayoldan**, başlangıç listesinden ya da zamanlanmış bir görevden tetikleyin. Şablonlar dahil. Adımlar birbirine değer aktarabilir: **kullanıcı girişi, kullanıcı seçimi** ve **seçili metni al** sonucunu birer değişkene yazar, sonraki adımlar da bunu bir URL'nin ya da metnin içinde `{ad}` olarak anar — «neyi arayacağımı sor, sonra da ara» iki adımdır.
- 🧹 **Sistem başlangıç öğeleri** — PC'nizde kendiliğinden başlayan her şey tek listede: ihtiyacınız olmayanı kapatın (silinmez, devre dışı bırakılır) ya da kendi başlangıç listenize devralın.
- 🔌 **Portlar** — dinlenen her TCP portu, onu tutan işlem ve geldiği proje ile yan yana: bir satıra çift tıklayarak `localhost:3000` adresini tarayıcıda açın, sağ tıklayarak portu serbest bırakın — portu tutan tüm işlemler alt işlemleriyle birlikte sonlandırılır. Varsayılan olarak yalnızca kendi proje klasörlerinizden başlatılan hizmetler görünür; böylece kapatmayı unuttuğunuz dev sunucusu sohbet uygulamaları ve sistem hizmetleri altında kaybolmaz.
- ⚡ **Hızlı panel** — tek bir kısayol (varsayılan `Ctrl+Alt+Space`) farenin zaten bulunduğu yerde bir kutu ızgarası açar: kendi eylemleriniz, ayrıca listeyi yeniden çalıştır, durdur, rahatsız etme ve pencereyi aç. Bir kutuya tıklayın ya da ok tuşlarıyla gidip Enter'a basın; **Esc** veya başka bir yere tıklamak paneli kapatır. Tepsi menüsünde de var, yani kısayolu silmek kısayolu kapatır, özelliği değil. Fareyi mi tercih edersiniz? **Orta düğmeyi basılı tutma**yı açın, klavyeye hiç dokunmadan açılsın — normal orta tık çalışmaya devam eder. Panel sayfalarını panel yöneticisinde kendiniz oluşturup kendiniz dizersiniz ve bir sayfadaki her karo tek bir işlemdir: ekranı kilitle, sesi kapat, uygulama aç, URL aç ve «bütün bir eylem grubunu çalıştır» böylece yan yana durur. Sayfalar çoğalınca gruplayın: **üst sıra bir kategori seçer, soldaki sütun o kategorinin sayfalarını listeler**; ikisini de sürükleyerek sıralarsınız. Hiçbir şey kategorilenmemişse hepsi *Kategorisiz* altında toplanır ve kategori sırası hiç görünmez. Hiç kullanmayacak mısınız? **Ayarlar → Hızlı paneli kullan** özelliğin kendisini kapatır; kısayol ve orta düğme birlikte.
- 🖱️ **Fare hareketleri** — **sağ tuşu** basılı tutup bir iz çizin, ilgili eylem çalışsın. Sekiz yön ve her adım türü (bütün bir eylem grubunu çalıştırmak dâhil). İz, siz çizerken ekranda görünür ve tuşu bıraktığınızda kaybolur. **Kurulumla birlikte on tanesi hazır gelir** (kopyala / yapıştır / geri / ileri / seçili metni ara (↑↓) / en alta git, ayrıca dört köşegen: küçült, büyüt, üstte tut ve kapat) — olduğu gibi kullanın ya da kendinize göre değiştirin. Bedeli baştan söyleyelim: etkin tek bir hareket bile sağ tuşu devralmaya yeter, dolayısıyla **sağ tuşla sürüklemek önce kısa bir duraklama ister** — basın, yaklaşık 0,2 sn kıpırdamadan bekleyin, sonra sürükleyin (Dosya Gezgini'nde dosyayı sağ tuşla sürüklemek, 3B uygulamada görüşü döndürmek). Sağ tuşa hiç dokunulmasını istemiyorsanız hareket yöneticisinin başlık satırında bir ana anahtar var. Gelen hareketleri kullanmak da zorunda değilsiniz: o anahtarı kapatın, çizimi WGestures / StrokesPlus / Quicker yapsın, eylemler burada kalsın — `Clockwork.exe --run-group "Odak"` ile. Bu anahtar **Ayarlar** sekmesinde de var: **Fare hareketlerini kullan**.

> **İstediğin an durdur** — sekme çubuğunun sağ ucundaki durdurma düğmesi (yalnızca bir şey çalışırken görünür), tepsi → **Çalışan eylemleri durdur** ya da genel acil durdurma kısayolu (varsayılan `Ctrl+Alt+Q`). Uzun beklemeler beklenmez, kesilir.

## Gereksinimler

| Konu | Ayrıntı |
| --- | --- |
| **Sistem** | Windows 10 / 11, x64 |
| **Kurulum** | Yok. Taşınabilir tek bir `Clockwork.exe` — istediğiniz klasöre koyun |
| **Yönetici hakkı** | Yalnızca «Oturum açınca başlat» ve **yönetici olarak çalıştır** diye işaretlediğiniz adımlar için |
| **Ayarlarınız** | exe'nin yanındaki `clockwork.settings.json` (ilk çalıştırmada o klasör salt okunursa; sonrasında bulunduğu yerde kalır `%APPDATA%\Clockwork\`) — hiçbir şey makineden çıkmaz |
| **Arayüz** | 18 dil ve koyu / açık tema. Dil ilk çalıştırmada Windows’u izler; tema koyu başlar ve Windows’u izleyecek şekilde ayarlanabilir |

**Sınırlar.** Kurulum olmayınca otomatik güncelleme de olmaz — yeni zip'i indirip exe'yi değiştirin. Sandbox başlatıcılar tuş gönderme, fare eylemleri, pencere işlemleri, çalışıyorsa-etkinleştir ve ses düzeyini engeller (net bir uyarı alırsınız; düz «program başlat» yine çalışır). Tuş yeniden atama ve metin genişletme kapsam dışıdır — o, AutoHotkey'in işidir.

## Başlarken

1. En son sürümü [Releases](https://github.com/rockbenben/Clockwork/releases) sayfasından indirin — iki yapı, üç indirme — ve elinizde kalan tek `Clockwork.exe`'yi herhangi bir klasöre bırakın.
   - **`Clockwork-<sürüm>-win-x64.zip`** — .NET çalışma zamanı dahil, her Windows 10/11 makinesinde olduğu gibi çalışır. Kararsızsanız ya da bilgisayar çevrimdışı veya kısıtlıysa bunu alın.
   - **`Clockwork-<sürüm>-win-x64-needs-dotnet10.zip`** — kurulu bir [.NET 10 Masaüstü Çalışma Zamanı](https://dotnet.microsoft.com/download/dotnet/10.0) gerektirir. İnternete bağlı bir bilgisayarda bir kez kurun, sonraki her güncelleme çok küçük bir indirme olsun.
   - **`Clockwork.exe`** — yukarıdaki zip ile aynı yapı, sadece zip'siz: tıklayıp çalıştırın ya da mevcut kopyanızın üzerine kopyalayıp güncelleyin. Çalışma zamanı yoksa Windows indirmeyi kendisi önerir.
2. Ayarlar penceresini açmak için çift tıklayın. Yüklenen örneklerin hepsi **işaretsiz** gelir — siz işaretlemeden hiçbir şey çalışmaz.
3. Her açılışta çalıştırmak için: **Ayarlar** sekmesinde **Oturum açınca başlat**'ı işaretleyin (yönetici haklarıyla bir zamanlanmış görev kaydeder, böylece açılışta yığınla UAC istemi olmaz).

Sonrasında tepside durur: simgeye çift tıklayınca pencere açılır, pencerenin kapat düğmesi ise onu yalnızca yeniden gizler. Gerçekten çıkmak için tepsinin sağ tık menüsündeki **Çıkış**'ı kullanın.

> [!IMPORTANT]
> **exe kod imzalı değil**, bu yüzden ilk çalıştırmada SmartScreen «Windows bilgisayarınızı korudu» der — **Daha fazla bilgi → Yine de çalıştır**'a tıklayın. Antivirüs de tepki verebilir: kayıt defteri Run anahtarları ve zamanlanmış görevler yazmak, bir başlangıç yöneticisinin tam olarak yaptığı iştir — ve aynı zamanda kötü amaçlı yazılımın yaptığı iştir; dışarıdan ayırt edilemez. Bunu güvene dayanarak kabul etmek istemiyorsanız, [kendiniz derleyin](../../CONTRIBUTING.md) — aynı sonuç, kendi ikili dosyanız. Her sürümde ayrıca bir `SHA256SUMS.txt` ve GitHub derleme kanıtı bulunur: `gh attestation verify <dosya> -R rockbenben/Clockwork`, indirilen dosyanın birinin dizüstü bilgisayarında değil, bu deponun CI'ında derlendiğini kanıtlar.

**Tam kılavuz** — her alan, her uç durum: [English](../USAGE.md) · [中文](../USAGE.zh.md)

## İpuçları

- **Bir satırı düzenlemek için çift tıklayın.** Yolları, işlemleri ve tarihleri elle yazmanız gerekmez: **satır sonundaki … düğmesi** ilgili seçiciyi açar (dosya, aranabilir işlem listesi, tarih); kısayolları ise **Yakala** ile basarak kaydedersiniz.
- **Sırayı değiştirmek için satırı sürükleyin** — üç listenin hepsinde ve grup düzenleyicinin adım listesinde geçerlidir; yukarı/aşağı düğmeleri de çalışmaya devam eder.
- **Kaydetmeden önce deneyin** — grup düzenleyicideki **▶ Bu adımı çalıştır** ve **▶ Grubu çalıştır**, o an ekranda olanı çalıştırır ve çalışırken düğme **■ Durdur**'a dönüşür.
- **Çoğalt**, seçili görev ya da grubun bir kopyasını hemen altına ekler — neredeyse aynısını sıfırdan kurmaktan hızlıdır. **Silme her yerde önce onay ister.**
- `Clockwork.exe`'ye çift tıklamak yalnızca pencereyi açar; başlangıç listesini yeniden **çalıştırmaz**. Bunun için tepsinin **Başlangıç listesini yeniden çalıştır**'ını kullanın.

## 365 Açık Kaynak Planı hakkında

[365 Açık Kaynak Planı](https://github.com/rockbenben/365opensource) kapsamındaki **#020** numaralı proje — bir kişi + yapay zeka, bir yılda 300'den fazla açık kaynak proje.

[Fikrinizi paylaşın →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
