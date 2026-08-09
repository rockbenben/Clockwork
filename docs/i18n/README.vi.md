<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Để những việc lặp đi lặp lại trên máy tính tự chạy**

Tự mở ứng dụng khi đăng nhập · nhắc nhở đúng giờ · một cú nhấp chạy cả chuỗi thao tác

**[⬇ Tải về cho Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — di động, không cần cài đặt

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../../LICENSE) [![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · **Tiếng Việt** · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

![Danh sách khởi động của Clockwork — một chuỗi các bước đăng nhập có thứ tự, mỗi bước có loại, độ trễ và điều kiện riêng](../../assets/screenshot.png)

## Nó làm được gì

- 🚀 **Danh sách khởi động** — mở lần lượt các ứng dụng thường dùng khi đăng nhập, mỗi bước có độ trễ, điều kiện ngày trong tuần và kiểu cửa sổ riêng; tiện thể đóng, đưa ra trước hoặc tắt tiếng.
- ⏰ **Tác vụ theo lịch** — một lời nhắc đúng giờ, đọc to nếu bạn muốn, hoặc một nhóm thao tác chạy im lặng. Nhấn **Có** có thể chạy chương trình, mở tệp hay URL, hoặc kích hoạt một nhóm.
- 🧹 **Mục khởi động hệ thống** — mọi thứ tự khởi động trên máy gom vào một danh sách: tắt những gì bạn không cần (vô hiệu hóa chứ không xóa) hoặc tiếp quản vào danh sách khởi động của riêng bạn.
- 🎛️ **Nhóm thao tác** — gói một chuỗi việc quen thuộc (Tập trung / Họp / Kết thúc / Trước khi ngủ…) và kích hoạt từ khay, một **phím tắt toàn cục**, danh sách khởi động hoặc một tác vụ theo lịch. Có sẵn mẫu dựng sẵn.

> **Dừng bất cứ lúc nào** — nút dừng ở cuối thanh thẻ (chỉ hiện khi có thứ gì đang chạy), khay hệ thống → **Dừng các hành động đang chạy**, hoặc phím dừng khẩn toàn cục (mặc định `Ctrl+Alt+Q`). Các khoảng chờ dài bị cắt ngắn chứ không phải ngồi đợi.

## Yêu cầu

| Hạng mục | Chi tiết |
| --- | --- |
| **Hệ thống** | Windows 10 / 11, x64 |
| **Cài đặt** | Không cần. Một tệp `Clockwork.exe` di động duy nhất — để vào thư mục bất kỳ |
| **Quyền admin** | Chỉ cần cho «Khởi động cùng đăng nhập» và các bước bạn đánh dấu **chạy với quyền admin** |
| **Cấu hình của bạn** | `clockwork.settings.json` cạnh tệp exe (hoặc `%APPDATA%\Clockwork\` nếu thư mục đó chỉ đọc) — không có gì rời khỏi máy |
| **Giao diện** | 18 ngôn ngữ, lần chạy đầu đi theo ngôn ngữ hiển thị của Windows |

**Giới hạn.** Không cài đặt cũng có nghĩa là không tự cập nhật — tải zip mới và thay tệp exe. Trình khởi chạy dạng sandbox chặn gửi phím, thao tác cửa sổ, kích hoạt-nếu-đang-chạy và âm lượng (bạn sẽ nhận thông báo rõ ràng; «chạy chương trình» thuần túy vẫn hoạt động). Gán lại phím và mở rộng văn bản nằm ngoài phạm vi — đó là việc của AutoHotkey.

## Bắt đầu

1. Tải bản mới nhất từ [Releases](https://github.com/rockbenben/Clockwork/releases) — hai bản dựng, ba lượt tải — rồi đặt tệp `Clockwork.exe` duy nhất bạn có vào thư mục bất kỳ.
   - **`Clockwork-<phiên bản>-win-x64.zip`** (~67 MB) — đã kèm .NET runtime, chạy được ngay trên mọi máy Windows 10/11. Phân vân, hoặc máy offline hay bị khóa không cài được gì, thì chọn gói này.
   - **`Clockwork-<phiên bản>-win-x64-needs-dotnet10.zip`** (~0,5 MB) — cần đã cài [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Cài một lần trên máy có mạng, về sau mỗi bản cập nhật chỉ tải 0,5 MB.
   - **`Clockwork.exe`** (~1,2 MB) — cùng bản dựng với gói zip ngay trên, chỉ là không bọc zip: bấm vào chạy luôn, hoặc ghi đè lên bản đang dùng để cập nhật. Thiếu runtime thì Windows sẽ tự mời bạn tải.
2. Nhấp đúp để mở cửa sổ cài đặt. Các mẫu được nạp đều **chưa được tick** — không có gì chạy cho tới khi bạn tự tick.
3. Để chạy mỗi lần khởi động máy: ở tab **Cài đặt**, tick **Khởi động cùng đăng nhập** (đăng ký một tác vụ theo lịch với quyền admin, để không phải chịu một loạt hộp thoại UAC khi khởi động).

Sau đó nó nằm trong khay hệ thống: nhấp đúp biểu tượng để mở cửa sổ, còn nút đóng chỉ thu nó lại. Muốn thoát hẳn, nhấp chuột phải vào khay và chọn **Thoát**.

> [!IMPORTANT]
> **Tệp exe không được ký số**, nên ở lần chạy đầu SmartScreen hiện «Windows đã bảo vệ PC của bạn» — bấm **More info → Run anyway**. Phần mềm diệt virus cũng có thể báo: ghi khoá Run trong registry và tác vụ theo lịch đúng là việc mà một trình quản lý khởi động phải làm — và cũng là việc mà mã độc hay làm; từ bên ngoài không phân biệt được. Nếu không muốn chấp nhận bằng niềm tin, hãy [tự build lấy](../../CONTRIBUTING.md) — kết quả như nhau, tệp nhị phân là của bạn.

**Hướng dẫn đầy đủ** — từng trường, từng trường hợp biên: [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Mẹo

- **Nhấp đúp một dòng để chỉnh sửa** nó. Đường dẫn, tiến trình, lối tắt và ngày đều được điền hộ: **Duyệt…**, **Chọn…** (trình chọn tiến trình có tìm kiếm), **Bắt phím**, **Chọn ngày**.
- **Kéo một dòng để đổi thứ tự** — trong cả ba danh sách và trong danh sách bước của trình chỉnh sửa nhóm; các nút lên/xuống vẫn dùng được.
- **Thử trước khi lưu** — **▶ Chạy bước này** và **▶ Chạy nhóm** trong trình chỉnh sửa nhóm chạy đúng những gì đang có trên màn hình, và nút đổi thành **■ Dừng** trong lúc chạy.
- **Nhân bản** tạo bản sao của tác vụ hay nhóm đang chọn ngay bên dưới — nhanh hơn dựng lại một mục gần giống. **Xóa luôn hỏi xác nhận trước**, ở mọi nơi.
- Nhấp đúp `Clockwork.exe` chỉ mở cửa sổ; nó **không** chạy lại danh sách khởi động. Dùng **Chạy lại danh sách khởi động** ở khay cho việc đó.

## Giới thiệu về 365 Open Source Plan

Dự án **#020** của [365 Open Source Plan](https://github.com/rockbenben/365opensource) — một người + AI, hơn 300 dự án mã nguồn mở trong một năm.

[Gửi ý tưởng của bạn →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
