<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Jalankan bagian-bagian berulang di PC Anda secara otomatis**

Luncurkan aplikasi otomatis saat login · pengingat terjadwal · satu ketukan untuk menjalankan seluruh rutinitas

**[⬇ Unduh untuk Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portabel, tanpa penginstal

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../../LICENSE) [![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · **Bahasa Indonesia** · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

![Daftar startup Clockwork — rangkaian langkah login yang berurutan, masing-masing dengan jenis, penundaan, dan syaratnya sendiri](../../assets/screenshot.png)

## Apa yang bisa dilakukannya

- 🚀 **Daftar startup** — membuka aplikasi sehari-hari Anda secara berurutan saat login, dengan penundaan, syarat hari, dan gaya jendela per langkah; sekalian menutup, memfokuskan, atau membisukan sesuatu di sepanjang jalan.
- ⏰ **Tugas terjadwal** — pengingat tepat waktu, dibacakan bila Anda mau, atau grup aksi yang berjalan diam-diam. Mengklik **Ya** dapat menjalankan program, membuka berkas atau URL, atau memicu sebuah grup.
- 🧹 **Item startup sistem** — semua yang berjalan otomatis di PC Anda dalam satu daftar: matikan yang tidak diperlukan (dinonaktifkan, bukan dihapus) atau ambil alih ke daftar startup Anda sendiri.
- 🎛️ **Grup aksi** — bungkus satu rutinitas (Fokus / Rapat / Beres-beres / Menjelang tidur…) dan picu dari tray, sebuah **tombol pintas global**, daftar startup, atau tugas terjadwal. Templat disertakan.

> **Hentikan kapan saja** — tombol hentikan di ujung kanan bilah tab (hanya muncul saat ada yang berjalan), baki → **Hentikan aksi yang berjalan**, atau tombol pintas darurat global (bawaan `Ctrl+Alt+Q`). Penantian panjang dipotong, bukan ditunggui.

## Persyaratan

| Aspek | Detail |
| --- | --- |
| **Sistem** | Windows 10 / 11, x64 |
| **Instalasi** | Tidak ada. Satu `Clockwork.exe` portabel — letakkan di folder mana pun |
| **Hak admin** | Hanya untuk «Mulai saat login» dan langkah yang Anda tandai **jalankan sebagai admin** |
| **Pengaturan Anda** | `clockwork.settings.json` di samping exe (atau `%APPDATA%\Clockwork\` bila folder itu hanya-baca) — tidak ada yang meninggalkan mesin |
| **Antarmuka** | 18 bahasa, mengikuti bahasa tampilan Windows pada jalankan pertama |

**Batasan.** Tanpa penginstal berarti tanpa pembaruan otomatis — unduh zip baru dan ganti exe-nya. Peluncur sandbox memblokir kirim-tombol, aksi jendela, aktifkan-jika-berjalan, dan volume (Anda akan mendapat pemberitahuan yang jelas; «luncurkan program» biasa tetap berfungsi). Pemetaan ulang tombol dan ekspansi teks di luar cakupan — itu pekerjaan AutoHotkey.

## Memulai

1. Unduh versi terbaru dari [Releases](https://github.com/rockbenben/Clockwork/releases) — dua build, tiga unduhan — lalu letakkan satu-satunya `Clockwork.exe` yang tersisa di folder mana pun.
   - **`Clockwork-<versi>-win-x64.zip`** (~67 MB) — runtime .NET sudah termasuk, langsung jalan di Windows 10/11 mana pun. Pilih ini kalau ragu, atau kalau PC-nya offline atau terkunci.
   - **`Clockwork-<versi>-win-x64-needs-dotnet10.zip`** (~0,5 MB) — butuh [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) yang sudah terpasang. Pasang sekali di PC yang online, setelah itu tiap pembaruan hanya unduhan 0,5 MB.
   - **`Clockwork.exe`** (~1,2 MB) — build yang sama dengan zip di atas, hanya tanpa zip: klik dan jalan, atau timpa salinan lama Anda untuk memperbarui. Kalau runtime-nya tidak ada, Windows menawarkan unduhannya.
2. Klik dua kali untuk membuka jendela pengaturan. Contoh yang dimuat semuanya **tidak dicentang** — tidak ada yang berjalan sampai Anda mencentangnya.
3. Agar berjalan setiap boot: pada tab **Pengaturan**, centang **Mulai saat login** (mendaftarkan tugas terjadwal dengan hak admin, sehingga tidak ada tumpukan prompt UAC saat boot).

Setelah itu ia bertengger di tray: klik dua kali ikonnya untuk membuka jendela, dan tombol tutup hanya menyembunyikannya lagi. Untuk benar-benar keluar, klik kanan tray lalu pilih **Keluar**.

> [!IMPORTANT]
> **File exe tidak ditandatangani**, jadi pada jalankan pertama SmartScreen menampilkan «Windows protected your PC» — klik **More info → Run anyway**. Antivirus pun bisa ikut memperingatkan: menulis kunci Run di registry dan tugas terjadwal memang persis pekerjaan sebuah pengelola startup — dan juga yang biasa dilakukan malware; dari luar keduanya tak terbedakan. Kalau tak mau menerimanya atas dasar percaya saja, [bangun sendiri](../../CONTRIBUTING.md) — hasilnya sama, binernya milik Anda.

**Panduan lengkap** — setiap kolom, setiap kasus pinggir: [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Tips

- **Klik dua kali sebuah baris untuk menyuntingnya**. Jalur, proses, pintasan, dan tanggal diisikan untuk Anda: **Telusuri…**, **Pilih…** (pemilih proses yang dapat dicari), **Rekam**, **Pilih tanggal**.
- **Seret sebuah baris untuk mengurutkannya** — di ketiga daftar dan di daftar langkah pada editor grup; tombol naik/turun tetap berfungsi.
- **Coba dulu sebelum menyimpan** — **▶ Jalankan langkah ini** dan **▶ Jalankan grup** di editor grup menjalankan apa yang sedang ada di layar, dan tombolnya berubah jadi **■ Hentikan** selama berjalan.
- **Duplikat** mengkloning tugas atau grup yang dipilih tepat di bawahnya — lebih cepat daripada menyusun ulang yang nyaris sama. **Penghapusan selalu bertanya lebih dulu**, di mana pun.
- Mengklik dua kali `Clockwork.exe` hanya membuka jendela; ia **tidak** menjalankan ulang daftar startup. Gunakan **Jalankan ulang daftar startup** di tray untuk itu.

## Tentang 365 Open Source Plan

Proyek **#020** dari [365 Open Source Plan](https://github.com/rockbenben/365opensource) — satu orang + AI, 300+ proyek open-source dalam setahun.

[Ajukan ide Anda →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
