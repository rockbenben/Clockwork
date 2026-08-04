<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**PC'nizin tekrarlayan işlerini otomatik pilota alın**

Oturum açınca uygulamalarınızı otomatik başlatın · zamanlı hatırlatıcılar · tek dokunuşla koca bir rutini çalıştırın

**[⬇ Windows için indir](https://github.com/rockbenben/Clockwork/releases/latest)** — taşınabilir, kurulum gerektirmez

[![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · **Türkçe** · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Bir Windows tepsi aracı: başlangıç başlatıcısı · hatırlatıcılar · sistem başlangıç öğeleri · eylem grupları

![Clockwork](../../assets/social-card.png)

Bilgisayar başında gününüze başlarken karşılaşılan rutin işleri üstlenen küçük bir Windows tepsi aracı:

- 🚀 **Başlangıç listesi** — oturum açınca günlük uygulamalarınızı sırayla otomatik açar (adım başına yönetici hakları, gecikmeler, yalnızca-belirli-hafta-günlerinde / yalnızca-saat-N'den-önce, pencere stili, çalışıyorsa-etkinleştir, yedek yollar) ve bu arada birkaç işi de halleder (pencereleri kapat veya odakla, tuş vuruşu / metin gönder, ses düzeyini ayarla…).
- ⏰ **Zamanlanmış görevler** — zamanı gelince bir hatırlatma gösterir; sesli okur; hafta gününe göre / her-N-günde / aylık tekrarlar; ya da "oturum açınca" tetiklenir. **Evet**'e tıklamak bir program çalıştırabilir, bir dosya (örneğin müzik) veya bir URL açabilir ya da bir eylem grubu çalıştırabilir. Ayrıca aralıklı çalıştırmayı ve tek seferlik zamanlamayı da destekler.
- 🧹 **Sistem başlangıç öğeleri** — **PC'nizde otomatik başlayan her şeyi** listeler ve ihtiyacınız olmayanları kapatır (silinmez, devre dışı bırakılır — istediğiniz zaman geri açın). Tek tıkla bir öğeyi kendi başlangıç listenize "devralır".
- 🎛️ **Eylem grupları** — bir dizi eylemi yeniden kullanılabilir bir grupta toplayın (Odak / Toplantı / Kapanış / Uyku vakti…) ve tepsiden, bir **genel kısayoldan**, başlangıç listesinden ya da bir hatırlatıcıdan tek tıkla tetikleyin. Yerleşik şablonlar dahildir.

Kurulum yok, tek klasörde tamamen taşınabilir, her şey fareyle yapılandırılabilir; koyu arayüz, yüksek DPI uyumlu.

> 📖 **Tam kılavuz:** [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Gereksinimler

- Windows 10 / 11 (x64)
- Kurulacak bir şey yok: .NET çalışma zamanı gömülü, kendi kendine yeten tek dosyalık bir `Clockwork.exe`.

## Başlarken

1. En son `Clockwork-<sürüm>.zip`'i [Releases](https://github.com/rockbenben/Clockwork/releases) sayfasından indirin ve arşivden çıkarın — içinde tek bir `Clockwork.exe` var; onu herhangi bir klasöre bırakın (taşınabilir — istediğiniz yere koyun). Kendiniz derlemek için aşağıdaki **Geliştiriciler için** bölümüne bakın.
2. Ayarlar penceresini açmak için **`Clockwork.exe`**'ye çift tıklayın.
   - **İlk çalıştırmada** başlangıç listesine ve hatırlatıcılara kendinize uyarlayabileceğiniz birkaç **örnek** yükler — hepsi başlangıçta işaretsizdir, yani siz işaretlemeden hiçbir şey çalışmaz. **Eylem grupları** sekmesi de hemen çalıştırılabilir iki grupla başlar (Kısa mola / Paydos · Gün sonu) — bunlar zaten *işaretlidir*, çünkü bir grup asla kendiliğinden tetiklenmez; yalnızca siz onu çalıştırdığınızda çalışır. Ayarlarınız exe'nin yanındaki `clockwork.settings.json` içinde durur — yalnızca yerel, asla depoya işlenmez.
3. Her açılışta çalıştırmak için: **Ayarlar** sekmesinde **Oturum açınca başlat**'a tıklayın (yönetici haklarıyla bir zamanlanmış görev kaydeder, böylece açılışta yığınla UAC istemi olmaz).

> Sessizce tepside durur. Pencereyi açmak için tepsi simgesine çift tıklayın; pencerenin kapat düğmesi onu yalnızca tepsiye gizler. Gerçekten çıkmak için tepsinin sağ tık menüsündeki **Çıkış**'ı kullanın.

> **İlk çalıştırmada uyarı çıkması normaldir.** exe kod imzalı değil, bu yüzden SmartScreen «Windows bilgisayarınızı korudu» der — **Daha fazla bilgi → Yine de çalıştır**'a tıklayın. Antivirüs de tepki verebilir: kayıt defteri Run anahtarları ve zamanlanmış görevler yazmak, bir başlangıç yöneticisinin tam olarak yaptığı iştir — ve aynı zamanda kötü amaçlı yazılımın yaptığı iştir; dışarıdan ayırt edilemez. Bunu güvene dayanarak kabul etmek istemiyorsanız, aşağıdaki **Geliştiriciler için** bölümüyle kendiniz derleyin: aynı sonuç, kendi ikili dosyanız.

## Ekran görüntüsü

![Screenshot](../../assets/screenshot.png)

## Beş sekme

Beş sekme; her alan tek tek [tam kılavuzda](../USAGE.md) anlatılıyor.

- **Başlangıç listesi** — adımlar oturum açarken yukarıdan aşağıya çalışır. Türler: program başlat · tuş gönder · metin gönder · ses · pencere işlemi · sistem komutu · eylem grubu · gecikme · mesaj. Her adımda adım sonrası gecikme, tekrar sayısı ve koşullar (yalnızca belirli günler / yalnızca saat N'den önce) vardır; programlarda ayrıca yönetici hakları, pencere stili, çalışıyorsa-öne-getir ve yedek yollar bulunur.
- **Zamanlanmış görevler** — bir saat (ya da "oturum açışta") × bir yineleme (haftanın günü / N günde bir / aylık / bir kez) × tek bir eylem: hatırlatma (ertele düğmeli Evet/Hayır penceresi ya da köşede bir kart, istenirse sesli okunur) veya sessizce çalışan bir eylem grubu. Ayrıca aralıklı çalıştırma, ısrarlı tekrar, kaçan tetiklemeyi telafi ve tepsiden rahatsız etme.
- **Sistem başlangıç öğeleri** — bilgisayarda kendiliğinden başlayan her şey (kayıt defteri Run anahtarları, Başlangıç klasörleri, zamanlanmış görevler): kapatma (devre dışı, silinmiş değil), kendi başlangıç listene devralma veya tamamen silme.
- **Eylem grupları** — yeniden kullanılabilir bir eylem demeti; tepsiden, **genel kısayoldan** (tekrar basınca o çalışma iptal olur), başlangıç listesindeki bir adımdan veya zamanlanmış bir görevden tetiklenir. Grup bütün olarak tekrarlanabilir ve başka gruplara başvurabilir (döngüsel başvurular kaydederken reddedilir); bir **mesaj** adımı Evet / Hayır ile gerisini durdurur.
- **Ayarlar** — başlangıç gecikmesi (0–600 sn, yalnızca açılışta), tepsiye küçültülmüş başlat, oturum açışta başlat, acil durdurma kısayolu, arayüz dili (18 dil), yapılandırmayı dışa / içe aktar.

> **İstediğin an durdur** — sekme çubuğunun sağ ucundaki **durdurma düğmesi** (yalnızca bir şey çalışırken görünür), tepsi → **Çalışan eylemleri durdur** ya da genel **acil durdurma kısayolu** (varsayılan `Ctrl+Alt+Q`). Uzun beklemeler (başlangıç gecikmesi, pencere bekleme) anında kesilir.

## İpuçları

- **Bir satırı düzenlemek için çift tıklayın.** Yolları / işlemleri / kısayolları / tarihleri doldururken elle yazmanız gerekmez: **Gözat…**, **Seç…** (aranabilir işlem seçici), **Yakala** ve **Tarih seç**.
- **Sırayı değiştirmek için satırı sürükleyin** — üç listenin hepsinde (başlangıç listesi, zamanlanmış görevler, eylem grupları) ve grup düzenleyicinin adım listesinde geçerlidir; yukarı/aşağı düğmeleri de çalışmaya devam eder.
- **Kaydetmeden önce deneyin** — grup düzenleyicide **▶ Bu adımı çalıştır** ve **▶ Grubu çalıştır** düğmeleri vardır; ikisi de o an ekranda olanı çalıştırır. Çalışırken düğme **■ Durdur**'a dönüşür ve düzenleyiciyi kapatmak da çalışmayı durdurur.
- **Çoğalt** (Zamanlanmış görevler / Eylem grupları sekmeleri), seçili satırın bir kopyasını hemen altına ekler — neredeyse aynı olan bir kaydı sıfırdan kurmaktan hızlıdır; çoğaltılan bir grup "… (kopya)" olarak adlandırılır.
- **Silme her yerde önce onay ister** — liste satırları, grup düzenleyicideki adımlar ve sistem başlangıç öğeleri.
- `Clockwork.exe`'ye çift tıklamak yalnızca ayarları açar — başlangıç listesini hemen **çalıştırmaz**; bunun için tepsinin **Başlangıç listesini yeniden çalıştır**'ını kullanın.
- **Normal şekilde başlatın** (çift tıklama / tepsi / zamanlanmış görev). Bazı sandbox / düşük ayrıcalıklı başlatıcılar düşük seviyeli çağrıları engeller, bu yüzden tuş gönderme / pencere eylemleri / çalışıyorsa-etkinleştir / işleme-metin-gönderme / ses çalışmayabilir (net bir uyarı alırsınız; düz "program başlat" etkilenmez).
- Yapılandırmanız `clockwork.settings.json`'dır (yalnızca yerel). Örneğe sıfırlamak için silin. Görev durumu `clockwork.state.json`'dır (o da yerel; silmesi güvenli).
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

[365 Açık Kaynak Planı](https://github.com/rockbenben/365opensource) kapsamındaki **#020** numaralı proje — bir kişi + yapay zeka, bir yılda 300'den fazla açık kaynak proje.

[Fikrinizi paylaşın →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)