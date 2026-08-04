<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Để những việc lặp đi lặp lại trên máy tính tự chạy**

Tự mở ứng dụng khi đăng nhập · nhắc nhở đúng giờ · một cú nhấp chạy cả chuỗi thao tác

**[⬇ Tải về cho Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — di động, không cần cài đặt

[![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · **Tiếng Việt** · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Công cụ khay hệ thống cho Windows: trình khởi động · nhắc nhở · mục khởi động hệ thống · nhóm thao tác

![Clockwork](../../assets/social-card.png)

Một công cụ nhỏ nằm ở khay hệ thống Windows, lo giúp bạn những phần lặp lại khi bắt đầu ngày làm việc bên máy tính:

- 🚀 **Danh sách khởi động** — tự động mở các ứng dụng thường dùng khi đăng nhập, theo thứ tự (quyền admin theo từng bước, độ trễ, chỉ vào một số ngày trong tuần / chỉ trước N giờ, kiểu cửa sổ, kích hoạt nếu đang chạy, đường dẫn dự phòng), và làm vài việc lặt vặt trên đường (đóng hoặc đưa cửa sổ ra trước, gửi phím / văn bản, chỉnh âm lượng…).
- ⏰ **Tác vụ theo lịch** — bật lời nhắc đúng giờ; đọc to; lặp theo ngày trong tuần / mỗi N ngày / hằng tháng; hoặc kích hoạt "khi đăng nhập". Nhấn **Có** có thể chạy một chương trình, mở một tệp (ví dụ nhạc) hay URL, hoặc chạy một nhóm thao tác. Cũng hỗ trợ chạy lặp lại theo khoảng thời gian và chạy một lần duy nhất.
- 🧹 **Mục khởi động hệ thống** — liệt kê **mọi thứ tự khởi động trên máy** và tắt những gì bạn không cần (bị vô hiệu hóa, không bị xóa — bật lại bất cứ lúc nào). Một cú nhấp "tiếp quản" một mục vào danh sách khởi động của riêng bạn.
- 🎛️ **Nhóm thao tác** — gói một chuỗi thao tác thành nhóm tái sử dụng (Tập trung / Họp / Kết thúc / Trước khi ngủ…) và kích hoạt bằng một cú nhấp từ khay, một **phím tắt toàn cục**, danh sách khởi động, hoặc một lời nhắc. Có sẵn các mẫu dựng sẵn.

Không cần cài đặt, một thư mục di động hoàn toàn, mọi thứ cấu hình bằng chuột; giao diện tối, tương thích DPI cao.

> 📖 **Hướng dẫn đầy đủ:** [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Yêu cầu

- Windows 10 / 11 (x64)
- Không cần cài gì: một tệp `Clockwork.exe` đơn lẻ, độc lập, đã đóng gói sẵn .NET runtime bên trong.

## Bắt đầu

1. Tải `Clockwork-<phiên bản>.zip` mới nhất từ [Releases](https://github.com/rockbenben/Clockwork/releases) rồi giải nén — bên trong là một tệp `Clockwork.exe` duy nhất; đặt nó vào thư mục bất kỳ (di động — để đâu cũng được). Muốn tự dựng, xem **Dành cho nhà phát triển** bên dưới.
2. Nhấp đúp **`Clockwork.exe`** để mở cửa sổ cài đặt.
   - Ở **lần chạy đầu tiên**, chương trình nạp vài **mẫu** trong danh sách khởi động và nhắc nhở để bạn chỉnh thành của mình — tất cả đều chưa được tick, nên không có gì chạy cho tới khi bạn tự tick. Tab **Nhóm thao tác** cũng khởi đầu với hai nhóm sẵn sàng chạy (Rời đi một lát / Kết thúc ngày làm) — hai nhóm này *đã được tick* sẵn, vì một nhóm không bao giờ tự kích hoạt; nó chỉ chạy khi bạn kích hoạt nó. Cài đặt của bạn nằm ở `clockwork.settings.json` cạnh tệp exe — chỉ trên máy, không đưa lên kho.
3. Để chạy mỗi lần khởi động máy: ở tab **Cài đặt**, nhấp **Khởi động cùng đăng nhập** (đăng ký một tác vụ theo lịch với quyền admin, để không phải chịu một loạt hộp thoại UAC khi khởi động).

> Nó nằm im trong khay hệ thống. Nhấp đúp biểu tượng khay để mở cửa sổ; nút đóng của cửa sổ chỉ thu vào khay. Muốn thoát hẳn, nhấp chuột phải vào khay và chọn **Thoát**.

> **Lần chạy đầu có cảnh báo là bình thường.** Tệp exe không được ký số nên SmartScreen hiện «Windows đã bảo vệ PC của bạn» — bấm **More info → Run anyway**. Phần mềm diệt virus cũng có thể báo: ghi khoá Run trong registry và tác vụ theo lịch đúng là việc mà một trình quản lý khởi động phải làm — và cũng là việc mà mã độc hay làm; từ bên ngoài không phân biệt được. Nếu không muốn chấp nhận bằng niềm tin, hãy tự build theo mục **Dành cho nhà phát triển** bên dưới: kết quả như nhau, tệp nhị phân là của bạn.

## Ảnh chụp màn hình

![Ảnh chụp màn hình](../../assets/screenshot.png)

## Năm tab

Năm thẻ; từng trường được giải thích trong [hướng dẫn đầy đủ](../USAGE.md).

- **Danh sách khởi động** — các bước chạy từ trên xuống khi đăng nhập. Loại: chạy chương trình · gửi phím · gửi văn bản · âm lượng · thao tác cửa sổ · lệnh hệ thống · nhóm hành động · chờ · thông báo. Mỗi bước có độ trễ sau bước, số lần lặp và điều kiện (chỉ một số thứ trong tuần / chỉ trước N giờ); chương trình còn có quyền quản trị, kiểu cửa sổ, kích hoạt-nếu-đang-chạy và đường dẫn dự phòng.
- **Tác vụ theo lịch** — một mốc giờ (hoặc "khi đăng nhập") × một chu kỳ (thứ trong tuần / mỗi N ngày / hằng tháng / một lần) × một hành động: lời nhắc (hộp thoại Có/Không kèm hoãn, hoặc thẻ ở góc màn hình, có thể đọc thành tiếng) hoặc một nhóm hành động chạy im lặng. Ngoài ra còn chạy theo khoảng, nhắc lại nhiều lần, bù lần kích hoạt bị lỡ và Không làm phiền từ khay hệ thống.
- **Mục khởi động hệ thống** — mọi thứ tự khởi động trên máy (khóa Run trong registry, thư mục Startup, tác vụ đã lên lịch): tắt đi (vô hiệu hóa chứ không xóa), tiếp quản vào danh sách khởi động của bạn, hoặc xóa hẳn.
- **Nhóm hành động** — một gói hành động dùng lại được, kích hoạt từ khay hệ thống, một **phím tắt toàn cục** (bấm lần nữa để hủy lần chạy đó), một bước trong danh sách khởi động, hoặc một tác vụ theo lịch. Nhóm có thể lặp cả nhóm và tham chiếu nhóm khác (tham chiếu vòng bị từ chối khi lưu); bước **thông báo** chặn phần còn lại bằng Có / Không.
- **Cài đặt** — độ trễ khởi động (0–600 giây, chỉ khi khởi động máy), khởi động thu nhỏ vào khay, chạy khi đăng nhập, phím dừng khẩn, ngôn ngữ giao diện (18), xuất / nhập cấu hình.

> **Dừng bất cứ lúc nào** — **nút dừng** ở cuối thanh thẻ (chỉ hiện khi có thứ gì đang chạy), khay hệ thống → **Dừng các hành động đang chạy**, hoặc **phím dừng khẩn** toàn cục (mặc định `Ctrl+Alt+Q`). Các khoảng chờ dài (độ trễ khởi động, chờ cửa sổ) bị ngắt ngay lập tức.

## Mẹo

- **Nhấp đúp một dòng để chỉnh sửa** nó. Khi điền đường dẫn / tiến trình / lối tắt / ngày, bạn không phải gõ tay: **Duyệt…**, **Chọn…** (trình chọn tiến trình có tìm kiếm), **Bắt phím**, và **Chọn ngày**.
- **Kéo một dòng để đổi thứ tự** — trong cả ba danh sách (danh sách khởi động, tác vụ theo lịch, nhóm hành động) và trong danh sách bước của trình chỉnh sửa nhóm; các nút lên/xuống vẫn dùng được.
- **Thử trước khi lưu** — trình chỉnh sửa nhóm có **▶ Chạy bước này** và **▶ Chạy nhóm**, cả hai đều chạy đúng những gì đang có trên màn hình. Khi đang chạy, nút đổi thành **■ Dừng**, và đóng trình chỉnh sửa cũng dừng nó.
- **Nhân bản** (tab Tác vụ theo lịch / Nhóm thao tác) tạo một bản sao của dòng đang chọn ngay bên dưới nó — nhanh hơn là dựng lại một mục gần giống; nhóm được nhân bản sẽ có tên "… (bản sao)".
- **Xóa luôn hỏi xác nhận trước**, ở mọi nơi — các dòng trong danh sách, các bước trong trình chỉnh sửa nhóm, và cả mục khởi động hệ thống.
- Nhấp đúp `Clockwork.exe` chỉ mở cài đặt — **không** chạy ngay danh sách khởi động; dùng **Chạy lại danh sách khởi động** ở khay cho việc đó.
- **Khởi chạy nó theo cách bình thường** (nhấp đúp / khay / tác vụ theo lịch). Một số trình khởi chạy dạng sandbox / hạn chế quyền chặn các lời gọi cấp thấp, nên gửi phím / thao tác cửa sổ / kích hoạt nếu đang chạy / gửi văn bản đến tiến trình / âm lượng có thể không hoạt động (bạn sẽ nhận thông báo rõ ràng; "khởi chạy chương trình" thuần túy không bị ảnh hưởng).
- Cấu hình của bạn là `clockwork.settings.json` (chỉ trên máy). Xóa nó để đặt lại về mẫu. Trạng thái tác vụ là `clockwork.state.json` (cũng chỉ trên máy; xóa được).
- Thêm một bước `.ahk` cần cài AutoHotkey. Phím tắt toàn cục / mở rộng văn bản nằm ngoài phạm vi — đó là thế mạnh của AutoHotkey.

## Dành cho nhà phát triển

C#/.NET WPF; mã nguồn ở `app/` (cần .NET 10 SDK). Các lớp: `Core/` logic thuần · `Native/` tương tác Win32 · `Engine/` thực thi · `ViewModels/` + `Views/` giao diện · `I18n/` + `Resources/` bản địa hóa (trung tính = nguồn tiếng Trung, một `Strings.<code>.resx` satellite cho mỗi ngôn ngữ).

- Chạy kiểm thử (xUnit):
  ```powershell
  dotnet test app.Tests/Clockwork.Tests.csproj
  ```
- Dựng tệp exe đơn lẻ, độc lập (các thuộc tính single-file / self-contained / nén đã đặt trong csproj):
  ```powershell
  dotnet publish app/Clockwork.csproj -c Release -r win-x64
  ```
  Kết quả: `app/bin/Release/net10.0-windows/win-x64/publish/Clockwork.exe`.
- **CI / phát hành** (GitHub Actions): push / PR sẽ dựng và chạy toàn bộ kiểm thử trên Windows runner; đẩy một thẻ `v*` (ví dụ `v2.0.0`) sẽ dựng, đóng dấu phiên bản tệp từ thẻ, tạo một GitHub Release và đính kèm `Clockwork-<thẻ>.zip` (chứa `Clockwork.exe`).

## Giới thiệu về 365 Open Source Plan

Dự án **#020** của [365 Open Source Plan](https://github.com/rockbenben/365opensource) — một người + AI, hơn 300 dự án mã nguồn mở trong một năm.

[Gửi ý tưởng của bạn →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)