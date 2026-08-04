<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Jalankan bagian-bagian berulang di PC Anda secara otomatis**

Luncurkan aplikasi otomatis saat login · pengingat terjadwal · satu ketukan untuk menjalankan seluruh rutinitas

**[⬇ Unduh untuk Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portabel, tanpa penginstal

[![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · **Bahasa Indonesia** · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Alat tray Windows: peluncur startup · pengingat · item startup sistem · grup aksi

![Clockwork](../../assets/social-card.png)

Alat tray Windows kecil yang mengurus bagian-bagian rutin saat memulai hari Anda di depan komputer:

- 🚀 **Daftar startup** — otomatis membuka aplikasi sehari-hari Anda saat login, secara berurutan (hak admin per-langkah, penundaan, hanya-pada-hari-tertentu / hanya-sebelum-pukul-N, gaya jendela, aktifkan-jika-sedang-berjalan, jalur cadangan), dan mengerjakan beberapa tugas kecil di sepanjang jalan (menutup atau memfokuskan jendela, mengirim penekanan tombol / teks, mengatur volume…).
- ⏰ **Tugas terjadwal** — memunculkan pengingat tepat waktu; membacakannya dengan lantang; mengulang menurut hari dalam seminggu / setiap-N-hari / bulanan; atau memicu "saat login". Mengklik **Ya** dapat menjalankan program, membuka berkas (mis. musik) atau sebuah URL, atau menjalankan grup aksi. Juga mendukung eksekusi berkala (interval) dan penjadwalan sekali saja.
- 🧹 **Item startup sistem** — mendaftar **semua yang berjalan otomatis di PC Anda** dan mematikan yang tidak Anda perlukan (dinonaktifkan, bukan dihapus — kembalikan kapan saja). Satu klik "mengambil alih" sebuah item ke daftar startup Anda sendiri.
- 🎛️ **Grup aksi** — menggabungkan serangkaian aksi menjadi satu grup yang dapat digunakan ulang (Fokus / Rapat / Beres-beres / Menjelang tidur…) dan memicunya dengan satu klik dari tray, sebuah **tombol pintas global**, daftar startup, atau sebuah pengingat. Templat bawaan disertakan.

Tanpa instalasi, sepenuhnya portabel dalam satu folder, semuanya dapat dikonfigurasi dengan mouse; antarmuka gelap, sadar high-DPI.

> 📖 **Panduan lengkap:** [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Persyaratan

- Windows 10 / 11 (x64)
- Tidak ada yang perlu diinstal: satu berkas `Clockwork.exe` yang mandiri (self-contained) dengan runtime .NET tergabung di dalamnya.

## Memulai

1. Unduh `Clockwork-<versi>.zip` terbaru dari [Releases](https://github.com/rockbenben/Clockwork/releases) lalu ekstrak — di dalamnya ada satu `Clockwork.exe`; letakkan di folder mana pun (portabel — taruh di mana saja). Untuk membangunnya sendiri, lihat **Untuk pengembang** di bawah.
2. Klik dua kali **`Clockwork.exe`** untuk membuka jendela pengaturan.
   - Pada **jalankan pertama** ia memuat beberapa **contoh** di daftar startup dan pengingat agar Anda dapat menyesuaikannya dengan milik Anda sendiri — semuanya tidak dicentang di awal, jadi tidak ada yang berjalan sampai Anda mencentangnya. Tab **Grup aksi** juga dimulai dengan dua grup siap-pakai (Pergi sebentar / Selesai kerja) — keduanya *sudah tercentang*, karena grup tidak pernah berjalan sendiri; ia hanya berjalan saat Anda memicunya. Pengaturan Anda tersimpan di `clockwork.settings.json` di samping exe — hanya lokal, tidak pernah di-commit.
3. Agar berjalan setiap boot: pada tab **Pengaturan**, klik **Mulai saat login** (mendaftarkan tugas terjadwal dengan hak admin, sehingga tidak ada tumpukan prompt UAC saat boot).

> Ia bertengger diam di tray. Klik dua kali ikon tray untuk membuka jendela; tombol tutup jendela hanya menyembunyikannya ke tray. Untuk benar-benar keluar, klik kanan tray lalu pilih **Keluar**.

> **Peringatan saat pertama dijalankan itu wajar.** File exe tidak ditandatangani, jadi SmartScreen menampilkan «Windows protected your PC» — klik **More info → Run anyway**. Antivirus pun bisa ikut memperingatkan: menulis kunci Run di registry dan tugas terjadwal memang persis pekerjaan sebuah pengelola startup — dan juga yang biasa dilakukan malware; dari luar keduanya tak terbedakan. Kalau tak mau menerimanya atas dasar percaya saja, bangun sendiri lewat **Untuk pengembang** di bawah: hasilnya sama, binernya milik Anda.

## Tangkapan layar

![Screenshot](../../assets/screenshot.png)

## Lima tab

Lima tab; setiap kolom dijelaskan satu per satu di [panduan lengkap](../USAGE.md).

- **Daftar startup** — langkah dijalankan dari atas ke bawah saat login. Jenis: jalankan program · kirim tombol · kirim teks · volume · aksi jendela · perintah sistem · grup aksi · jeda · pesan. Setiap langkah punya jeda setelahnya, jumlah pengulangan, dan syarat (hanya hari tertentu / hanya sebelum pukul N); program juga punya hak admin, gaya jendela, aktifkan-jika-berjalan, dan jalur cadangan.
- **Tugas terjadwal** — sebuah waktu (atau "saat login") × pengulangan (hari dalam minggu / tiap N hari / bulanan / sekali) × satu aksi: pengingat (dialog Ya/Tidak dengan tunda, atau kartu di sudut layar, bisa dibacakan) atau grup aksi yang berjalan diam-diam. Ditambah eksekusi berkala, desakan berulang, menyusul pemicu yang terlewat, dan Jangan Ganggu dari baki sistem.
- **Item startup sistem** — semua yang berjalan otomatis di PC (kunci Run registry, folder Startup, tugas terjadwal): matikan (dinonaktifkan, bukan dihapus), ambil alih ke daftar startup Anda sendiri, atau hapus permanen.
- **Grup aksi** — paket aksi yang bisa dipakai ulang, dipicu dari baki sistem, **tombol pintas global** (tekan lagi untuk membatalkan jalannya), langkah di daftar startup, atau tugas terjadwal. Grup bisa mengulang seluruhnya dan mereferensikan grup lain (referensi melingkar ditolak saat menyimpan); langkah **pesan** menahan sisanya dengan Ya / Tidak.
- **Pengaturan** — jeda startup (0–600 detik, hanya saat boot), mulai terminimalkan ke baki, jalankan saat login, tombol pintas darurat, bahasa antarmuka (18), ekspor / impor konfigurasi.

> **Hentikan kapan saja** — **tombol hentikan** di ujung kanan bilah tab (hanya muncul saat ada yang berjalan), baki → **Hentikan aksi yang berjalan**, atau **tombol pintas darurat** global (bawaan `Ctrl+Alt+Q`). Penantian panjang (jeda startup, menunggu jendela) langsung diputus.

## Tips

- **Klik dua kali sebuah baris untuk menyuntingnya**. Ketika mengisi jalur / proses / pintasan / tanggal Anda tidak perlu mengetik dengan tangan: **Telusuri…**, **Pilih…** (pemilih proses yang dapat dicari), **Rekam**, dan **Pilih tanggal**.
- **Seret sebuah baris untuk mengurutkannya** — di ketiga daftar (daftar startup, tugas terjadwal, grup aksi) dan di daftar langkah pada editor grup; tombol naik/turun tetap berfungsi.
- **Coba dulu sebelum menyimpan** — editor grup punya **▶ Jalankan langkah ini** dan **▶ Jalankan grup**, keduanya menjalankan apa yang sedang ada di layar. Selama berjalan tombolnya berubah jadi **■ Hentikan**, dan menutup editor juga menghentikannya.
- **Duplikat** (tab Tugas terjadwal / Grup aksi) mengkloning baris yang dipilih tepat di bawahnya — lebih cepat daripada menyusun ulang yang nyaris sama; grup hasil duplikasi diberi nama "… (salinan)".
- **Penghapusan selalu bertanya lebih dulu**, di mana pun — baris daftar, langkah di dalam editor grup, dan item startup sistem.
- Mengklik dua kali `Clockwork.exe` hanya membuka pengaturan — ia **tidak** langsung menjalankan daftar startup; gunakan **Jalankan ulang daftar startup** di tray untuk itu.
- **Luncurkan secara normal** (klik dua kali / tray / tugas terjadwal). Beberapa peluncur sandbox / berhak-akses-berkurang memblokir panggilan tingkat rendah, sehingga kirim-tombol / aksi jendela / aktifkan-jika-berjalan / kirim-teks-ke-proses / volume mungkin tidak berfungsi (Anda akan mendapat pemberitahuan yang jelas; "luncurkan program" biasa tidak terpengaruh).
- Konfigurasi Anda adalah `clockwork.settings.json` (hanya lokal). Hapus untuk mengatur ulang ke contoh. Status tugas adalah `clockwork.state.json` (juga lokal; aman dihapus).
- Menambah langkah `.ahk` membutuhkan AutoHotkey terinstal. Tombol pintas global / ekspansi teks di luar cakupan — itulah keunggulan AutoHotkey.

## Untuk pengembang

C#/.NET WPF; sumber di `app/` (membutuhkan .NET 10 SDK). Lapisan: `Core/` logika murni · `Native/` interop Win32 · `Engine/` eksekusi · `ViewModels/` + `Views/` UI · `I18n/` + `Resources/` pelokalan (neutral = sumber Tionghoa, satu `Strings.<code>.resx` satelit per bahasa).

- Menjalankan tes (xUnit):
  ```powershell
  dotnet test app.Tests/Clockwork.Tests.csproj
  ```
- Membangun exe berkas tunggal yang mandiri (single-file / self-contained / kompresi diatur di csproj):
  ```powershell
  dotnet publish app/Clockwork.csproj -c Release -r win-x64
  ```
  Keluaran: `app/bin/Release/net10.0-windows/win-x64/publish/Clockwork.exe`.
- **CI / rilis** (GitHub Actions): push / PR membangun dan menjalankan semua tes pada Windows runner; men-push tag `v*` (mis. `v2.0.0`) membangun, mencap versi berkas dari tag, membuat GitHub Release dan melampirkan `Clockwork-<tag>.zip` (berisi `Clockwork.exe`).

## Tentang 365 Open Source Plan

Proyek **#020** dari [365 Open Source Plan](https://github.com/rockbenben/365opensource) — satu orang + AI, 300+ proyek open-source dalam setahun.

[Ajukan ide Anda →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)