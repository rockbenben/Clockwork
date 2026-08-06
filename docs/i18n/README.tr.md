<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**PC'nizin tekrarlayan işlerini otomatik pilota alın**

Oturum açınca uygulamalarınızı otomatik başlatın · zamanlı hatırlatıcılar · tek dokunuşla koca bir rutini çalıştırın

**[⬇ Windows için indir](https://github.com/rockbenben/Clockwork/releases/latest)** — taşınabilir, kurulum gerektirmez

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../../LICENSE) [![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · **Türkçe** · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

![Clockwork'ün başlangıç listesi — her biri kendi türü, gecikmesi ve koşullarıyla sıralı oturum açma adımları](../../assets/screenshot.png)

## Neler yapar

- 🚀 **Başlangıç listesi** — oturum açınca günlük uygulamalarınızı sırayla açar; her adımda gecikme, hafta günü koşulu ve pencere stili verilebilir, bu arada pencere kapatır, öne getirir veya sesi kapatır.
- ⏰ **Zamanlanmış görevler** — zamanı gelince bir hatırlatma (isterseniz sesli okunur) ya da sessizce çalışan bir eylem grubu. **Evet**'e tıklamak bir program çalıştırabilir, bir dosya veya URL açabilir ya da bir grubu tetikleyebilir.
- 🧹 **Sistem başlangıç öğeleri** — PC'nizde kendiliğinden başlayan her şey tek listede: ihtiyacınız olmayanı kapatın (silinmez, devre dışı bırakılır) ya da kendi başlangıç listenize devralın.
- 🎛️ **Eylem grupları** — bir rutini paketleyin (Odak / Toplantı / Kapanış / Uyku vakti…) ve tepsiden, bir **genel kısayoldan**, başlangıç listesinden ya da zamanlanmış bir görevden tetikleyin. Şablonlar dahil.

> **İstediğin an durdur** — sekme çubuğunun sağ ucundaki durdurma düğmesi (yalnızca bir şey çalışırken görünür), tepsi → **Çalışan eylemleri durdur** ya da genel acil durdurma kısayolu (varsayılan `Ctrl+Alt+Q`). Uzun beklemeler beklenmez, kesilir.

## Gereksinimler

| Konu | Ayrıntı |
| --- | --- |
| **Sistem** | Windows 10 / 11, x64 |
| **Kurulum** | Yok. .NET çalışma zamanı gömülü tek bir `Clockwork.exe` — istediğiniz klasöre koyun |
| **Yönetici hakkı** | Yalnızca «Oturum açınca başlat» ve **yönetici olarak çalıştır** diye işaretlediğiniz adımlar için |
| **Ayarlarınız** | exe'nin yanındaki `clockwork.settings.json` (o klasör salt okunursa `%APPDATA%\Clockwork\`) — hiçbir şey makineden çıkmaz |
| **Arayüz** | 18 dil, ilk çalıştırmada Windows görüntü dilinizi izler |

**Sınırlar.** Kurulum olmayınca otomatik güncelleme de olmaz — yeni zip'i indirip exe'yi değiştirin. Sandbox başlatıcılar tuş gönderme, pencere işlemleri, çalışıyorsa-etkinleştir ve ses düzeyini engeller (net bir uyarı alırsınız; düz «program başlat» yine çalışır). Tuş yeniden atama ve metin genişletme kapsam dışıdır — o, AutoHotkey'in işidir.

## Başlarken

1. En son `Clockwork-<sürüm>.zip`'i [Releases](https://github.com/rockbenben/Clockwork/releases) sayfasından indirin, arşivden çıkarın ve içindeki tek `Clockwork.exe`'yi herhangi bir klasöre bırakın.
2. Ayarlar penceresini açmak için çift tıklayın. Yüklenen örneklerin hepsi **işaretsiz** gelir — siz işaretlemeden hiçbir şey çalışmaz.
3. Her açılışta çalıştırmak için: **Ayarlar** sekmesinde **Oturum açınca başlat**'ı işaretleyin (yönetici haklarıyla bir zamanlanmış görev kaydeder, böylece açılışta yığınla UAC istemi olmaz).

Sonrasında tepside durur: simgeye çift tıklayınca pencere açılır, pencerenin kapat düğmesi ise onu yalnızca yeniden gizler. Gerçekten çıkmak için tepsinin sağ tık menüsündeki **Çıkış**'ı kullanın.

> [!IMPORTANT]
> **exe kod imzalı değil**, bu yüzden ilk çalıştırmada SmartScreen «Windows bilgisayarınızı korudu» der — **Daha fazla bilgi → Yine de çalıştır**'a tıklayın. Antivirüs de tepki verebilir: kayıt defteri Run anahtarları ve zamanlanmış görevler yazmak, bir başlangıç yöneticisinin tam olarak yaptığı iştir — ve aynı zamanda kötü amaçlı yazılımın yaptığı iştir; dışarıdan ayırt edilemez. Bunu güvene dayanarak kabul etmek istemiyorsanız, [kendiniz derleyin](../../CONTRIBUTING.md) — aynı sonuç, kendi ikili dosyanız.

**Tam kılavuz** — her alan, her uç durum: [English](../USAGE.md) · [中文](../USAGE.zh.md)

## İpuçları

- **Bir satırı düzenlemek için çift tıklayın.** Yollar, işlemler, kısayollar ve tarihler sizin için doldurulur: **Gözat…**, **Seç…** (aranabilir işlem seçici), **Yakala**, **Tarih seç**.
- **Sırayı değiştirmek için satırı sürükleyin** — üç listenin hepsinde ve grup düzenleyicinin adım listesinde geçerlidir; yukarı/aşağı düğmeleri de çalışmaya devam eder.
- **Kaydetmeden önce deneyin** — grup düzenleyicideki **▶ Bu adımı çalıştır** ve **▶ Grubu çalıştır**, o an ekranda olanı çalıştırır ve çalışırken düğme **■ Durdur**'a dönüşür.
- **Çoğalt**, seçili görev ya da grubun bir kopyasını hemen altına ekler — neredeyse aynısını sıfırdan kurmaktan hızlıdır. **Silme her yerde önce onay ister.**
- `Clockwork.exe`'ye çift tıklamak yalnızca pencereyi açar; başlangıç listesini yeniden **çalıştırmaz**. Bunun için tepsinin **Başlangıç listesini yeniden çalıştır**'ını kullanın.

## 365 Açık Kaynak Planı hakkında

[365 Açık Kaynak Planı](https://github.com/rockbenben/365opensource) kapsamındaki **#020** numaralı proje — bir kişi + yapay zeka, bir yılda 300'den fazla açık kaynak proje.

[Fikrinizi paylaşın →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
