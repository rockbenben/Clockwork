<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Jalankan bagian-bagian berulang di PC Anda secara otomatis**

Luncurkan aplikasi otomatis saat login · pengingat terjadwal · satu ketukan untuk menjalankan seluruh rutinitas

</div>

<div align="center">

[English](../../README.md) · [简体中文](README.zh-Hans.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · **Bahasa Indonesia** · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Alat tray Windows: peluncur startup · pengingat · item startup sistem · grup aksi

[![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

![Clockwork](../../assets/social-card.png)

Alat tray Windows kecil yang mengurus bagian-bagian rutin saat memulai hari Anda di depan komputer:

- 🚀 **Daftar startup** — otomatis membuka aplikasi sehari-hari Anda saat login, secara berurutan (hak admin per-langkah, penundaan, hanya-pada-hari-tertentu / hanya-sebelum-pukul-N, gaya jendela, aktifkan-jika-sedang-berjalan, jalur cadangan), dan mengerjakan beberapa tugas kecil di sepanjang jalan (menutup atau memfokuskan jendela, mengirim penekanan tombol / teks, mengatur volume…).
- ⏰ **Tugas terjadwal** — memunculkan pengingat tepat waktu; membacakannya dengan lantang; mengulang menurut hari dalam seminggu / setiap-N-hari / bulanan; atau memicu "saat login". Mengklik **Ya** dapat menjalankan program, membuka berkas (mis. musik) atau sebuah URL, atau menjalankan grup aksi. Juga mendukung eksekusi berkala (interval) dan penjadwalan sekali saja.
- 🧹 **Item startup sistem** — mendaftar **semua yang berjalan otomatis di PC Anda** dan mematikan yang tidak Anda perlukan (dinonaktifkan, bukan dihapus — kembalikan kapan saja). Satu klik "mengambil alih" sebuah item ke daftar startup Anda sendiri.
- 🎛️ **Grup aksi** — menggabungkan serangkaian aksi menjadi satu grup yang dapat digunakan ulang (Fokus / Rapat / Beres-beres / Menjelang tidur…) dan memicunya dengan satu klik dari tray, sebuah **tombol pintas global**, daftar startup, atau sebuah pengingat. Templat bawaan disertakan.

Tanpa instalasi, sepenuhnya portabel dalam satu folder, semuanya dapat dikonfigurasi dengan mouse; antarmuka gelap, sadar high-DPI.

> 📖 **Panduan lengkap:** [English](../USAGE.md) · [中文](../USAGE.zh-CN.md)

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

### Daftar startup

Sebuah **daftar langkah yang berurutan** dijalankan dari atas ke bawah saat login. Klik **Tambah ▾** untuk memilih jenis; tambah/hapus/urutkan ulang dengan bebas; setiap langkah dapat diaktifkan/dinonaktifkan, diberi **penundaan pasca-langkah**, **jumlah pengulangan** (mengulanginya N kali), dan kondisi (**hanya pada hari tertentu / hanya sebelum pukul N**). Jenis langkah:

- **Luncurkan program** — target (**Telusuri…** untuk memilih berkas) / argumen / direktori kerja (kosongkan = folder target) / admin. Target dapat berupa `.exe`, dokumen, pintasan, atau URL; sebuah `.ps1` berjalan melalui PowerShell. Lanjutan: **gaya jendela** (diminimalkan / dimaksimalkan / disembunyikan), **aktifkan jika sudah berjalan** (bawa ke depan alih-alih meluncurkan ulang; nama proses melalui **Pilih…**), **jalur cadangan** (satu jalur lengkap per baris; yang pertama ada yang digunakan — praktis ketika jalur instalasi berbeda antar mesin).
- **Kirim tombol** — mis. Win+D, Alt+K, Ctrl+Enter, F5 (**Rekam** untuk merekam pintasan dengan menekannya).
- **Kirim teks** — mengetikkan sebuah string ke jendela yang terfokus (atau ke sebuah **proses target** yang dipilih melalui **Pilih…**).
- **Volume** — bisukan / bunyikan / atur level.
- **Aksi jendela** — menurut nama proses (**Pilih…**, dapat dicari): tutup / minimalkan / maksimalkan / bawa-ke-depan / bawa-ke-depan-dan-kirim-tombol; aplikasi yang lambat dapat **menunggu hingga N detik agar jendela muncul**.
- **Perintah sistem** — tampilkan desktop / kunci / matikan monitor / kosongkan tempat sampah / bersihkan clipboard / buka Pengaturan / Task Manager / tangkapan layar / tidur / hibernasi / keluar akun / mulai ulang / matikan (tiga yang terakhir mengonfirmasi lebih dulu).
- **Penundaan** — cukup menunggu N detik sebelum langkah berikutnya.
- **Grup aksi** — menjalankan grup aksi yang telah ditentukan; jumlah pengulangannya menyatakan berapa kali *referensi ini* memicu (grup itu sendiri juga bisa berulang secara internal — lihat **Grup aksi** di bawah).

> **Penundaan startup** (tab Pengaturan, hanya saat boot): tunggu sejumlah detik tetap setelah login agar "badai login" (perebutan disk/CPU dari setiap autostart) mereda sebelum daftar dijalankan; menjalankan ulang secara manual tidak terpengaruh. Naikkan (0–600 dtk) jika segala sesuatu mulai terlalu awal.

> **Hentikan kapan saja** — **tombol berhenti** di ujung kanan bilah tab jendela (hanya muncul saat ada yang berjalan), tray → **Hentikan aksi yang berjalan**, atau **tombol pintas panik** global (diatur pada tab Pengaturan; bawaan `Ctrl+Alt+Q`). Apa pun yang sedang berjalan berhenti setelah aksi saat ini; penungguan yang lama (penundaan startup, menunggu jendela) diinterupsi seketika.

### Tugas terjadwal

Atur sebuah **waktu** (atau beralih ke **saat login**), sebuah **pengulangan** (hari dalam seminggu / setiap-N-hari / bulanan / **sekali saja pada tanggal tertentu**), lalu pilih satu **aksi**: **memunculkan pengingat** atau **menjalankan grup aksi secara senyap**. Hanya kolom milik aksi yang dipilih yang tetap tampil, jadi Anda tidak akan pernah mengisi kotak yang tidak berpengaruh.

Pengingat dengan aksi **Saat-Ya** (jalankan program / buka berkas / URL / jalankan grup aksi) memunculkan dialog **Ya / Tidak** dengan tombol **Tunda** (bawaan 10 mnt, menu ▾ 5–60 mnt); selebihnya meluncur masuk sebagai **kartu pengingat** di sudut (menutup otomatis setelah detik yang dikonfigurasi, **0 = tetap ada hingga Anda menutupnya**). Teks dapat dibacakan dengan lantang.

**Jalan berinterval** mengubah satu tugas menjadi jadwal sepanjang hari: *setiap N menit hingga HH:mm* (kosong = akhir hari). Berbeda dengan **desakan berulang** yang berhenti begitu Anda mengonfirmasi, jalan berinterval terus berlanjut setelah Anda menjawab — justru itu yang membuat grup senyap berguna sebagai penjajakan berkala. Interval tidak melewati tengah malam; besok mulai lagi dari waktu tugas itu sendiri. Kemajuannya disimpan, jadi memulai ulang aplikasi tengah hari tetap mempertahankan putaran sisa hari itu.

**Sekali saja** memicu pada tanggalnya lalu **melepas centangnya sendiri**, tetap berada di daftar — ubah tanggalnya dan centang lagi untuk memakainya kembali. Ia menunggu desakan atau penundaan selesai sebelum mematikan diri, jadi tidak pernah memutus pengiriman di tengah jalan.

Dialog yang tak dijawab tidak menyumbat antrean dan tidak hilang: setelah paling lama satu menit ia berubah menjadi **penundaan otomatis 10 menit** lalu muncul kembali. Status ini ditulis ke disk seperti penundaan biasa — bertahan melewati mulai ulang, dan dengan **kejar jika terlewat** aktif ia tetap terpicu sekali lagi keesokan harinya meski malam melewati tengah malam. Pemicuan berulang dari satu pengingat berbagi satu kartu (bertanda **×N**), dan kartu yang ditutup atau kedaluwarsa bisa ditampilkan lagi dari menu **Terbaru** di tray.

Lanjutan: **penutupan otomatis**, **rengekan berulang** (memunculkan kembali setiap N menit hingga tenggat), **penundaan pasca-pemicu + jitter acak**, **tenggang** (menangkap pemicu yang terlewat karena mati/tidur singkat), **kejar jika terlewat** (memunculkan sekali lagi setelah hibernasi/mati melewatkannya), dan sebuah **tanggal jangkar** untuk setiap-N-hari (**Pilih tanggal**). "Terpicu hari ini" dan "ditunda hingga" bertahan melalui mulai ulang (`clockwork.state.json`), sehingga sebuah penundaan terbawa melintasi mulai ulang dan tidak ada yang terpicu ganda.

Perlu fokus atau ikut rapat? Tray menawarkan **Jeda pengingat selama 1 / 2 / 4 jam** (Jangan Ganggu): semuanya (termasuk grup senyap) ditekan dan otomatis dilanjutkan saat waktunya habis.

### Item startup sistem

Mendaftar **semua yang berjalan otomatis** (kunci Run registri, folder Startup, tugas terjadwal). Hapus centang **Aktifkan** untuk mematikan sebuah item — **dinonaktifkan, bukan dihapus; centang kembali untuk memulihkan** (berlaku seketika). Item yang ditandai **butuh admin** meminta untuk diluncurkan ulang dengan hak lebih tinggi. Item sistem / kebijakan / sekali-pakai (Group-Policy Run, RunOnce, Winlogon, Active Setup) tidak dapat disentuh dan **disembunyikan secara bawaan** — centang **Tampilkan item sistem / hanya-baca** untuk melihatnya (berwarna abu-abu). Klik kanan sebuah baris untuk **Ambil alih ke daftar mulai** (menyerahkan item itu ke Clockwork; hanya kunci Run registri dan item folder Startup) atau **Hapus dari sistem** (menghapus entri itu selamanya — bertanya lebih dulu, dan tidak dapat dibatalkan; menghapus centang adalah opsi yang dapat dibalikkan). Sebuah **filter** di atas mencari menurut nama / perintah; arahkan kursor ke perintah yang terpotong untuk membacanya secara penuh.

### Grup aksi

Menggabungkan aksi-aksi menjadi satu grup yang dapat digunakan ulang. **Tambah ▾** memulai satu dari **templat bawaan** (Fokus / Rapat / Beres-beres / Menjelang tidur / Meninggalkan meja / Tangkapan layar / Pengingat duduk lama) — sesuaikan nama proses lalu simpan. Sebuah grup **hanya mendefinisikan aksi**; picu dengan empat cara: dari tray (**Jalankan: <grup>**), sebuah **tombol pintas global**, sebagai **langkah grup-aksi** dalam daftar startup (saat boot), atau dari sebuah tugas terjadwal (**Saat-Ya / grup senyap**). Sebuah grup hanya menjalankan satu salinan pada satu waktu.

**Pengulangan.** Sebuah grup dapat **berulang secara utuh** (jumlah pengulangan + jeda antar putaran, diatur di editor grup). Untuk mengulang hanya *sebagian* urutan, taruh langkah-langkah itu di grup tersendiri lalu rujuk dengan langkah **grup aksi** yang jumlah pengulangannya Anda tentukan — grup boleh merujuk grup, dan saat menyimpan rujukan berputar ditolak dengan rantainya diuraikan (`A → B → A`). Ketiga tuas pengulangan saling dikalikan: per langkah × per rujukan × seluruh grup. Satu kali jalan diberi sekring **5000 langkah**, sehingga kombinasi yang berlebihan berhenti alih-alih berjalan tanpa akhir.

Langkah **pesan** adalah gerbang konfirmasi: menjawab **Tidak** membatalkan sisa grup *beserta putaran yang tersisa*, dan menjalar ke luar ke grup yang merujuknya — menolak sekali sudah cukup, bahkan di dalam subgrup yang diulang ×N.

> **Tombol pintas global** — di editor grup, klik kotak tombol pintas dan tekan sebuah pintasan (mis. `Ctrl+Alt+F`) untuk menjalankan grup itu dari mana saja, tanpa perlu menu. Esc membatalkan, Delete mengosongkan. Grup yang dinonaktifkan melepaskan kombinasinya; kombinasi yang dicadangkan sistem (Alt+F4, Ctrl+Shift+Esc…) dan kombinasi yang sudah dipakai oleh grup lain atau tombol pintas panik ditolak dengan sebuah pemberitahuan.

### Pengaturan

**Penundaan startup** (0–600 dtk, hanya saat boot), **mulai terminimalkan ke tray**, **tombol pintas panik** (klik kotaknya dan tekan pintasan Anda; Esc membatalkan, Delete mengosongkan; bawaan `Ctrl+Alt+Q`), dan **bahasa antarmuka** (Tionghoa Sederhana, Inggris, 日本語 dan 15 lagi — total 18; beralih akan memulai ulang aplikasi untuk menerapkannya).

**Ekspor konfigurasi / Impor konfigurasi** — pindahkan seluruh penyiapan Anda ke PC lain atau simpan sebagai cadangan. Ekspor menuliskan salinan `clockwork.settings.json` ke mana pun Anda suka; impor menggantikan **semuanya** (daftar startup / tugas terjadwal / grup aksi / pengaturan), jadi ia mengonfirmasi lebih dulu, mencadangkan konfigurasi saat ini ke `clockwork.settings.json.bak`, dan memulai ulang aplikasi untuk menerapkannya.

## Tips

- **Klik dua kali sebuah baris untuk menyuntingnya**. Ketika mengisi jalur / proses / pintasan / tanggal Anda tidak perlu mengetik dengan tangan: **Telusuri…**, **Pilih…** (pemilih proses yang dapat dicari), **Rekam**, dan **Pilih tanggal**.
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

Proyek **#020** dari [365 Open Source Plan](https://github.com/rockbenben/365opensource) — satu orang + AI, 300+ proyek open-source dalam setahun. [Ajukan ide Anda →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)