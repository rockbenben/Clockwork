<div align="center">

<img src="../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Để những việc lặp đi lặp lại trên máy tính tự chạy**

Tự mở ứng dụng khi đăng nhập · nhắc nhở đúng giờ · một cú nhấp chạy cả chuỗi thao tác

</div>

<div align="center">

[English](../README.md) · [简体中文](README.zh-CN.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · **Tiếng Việt** · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> 365 Open-Source Plan #020 · Công cụ khay hệ thống cho Windows: trình khởi động · nhắc nhở · mục khởi động hệ thống · nhóm thao tác

![Clockwork](../assets/social-card.png)

Một công cụ nhỏ nằm ở khay hệ thống Windows, lo giúp bạn những phần lặp lại khi bắt đầu ngày làm việc bên máy tính:

- 🚀 **Danh sách khởi động** — tự động mở các ứng dụng thường dùng khi đăng nhập, theo thứ tự (quyền admin theo từng bước, độ trễ, chỉ vào một số ngày trong tuần / chỉ trước N giờ, kiểu cửa sổ, kích hoạt nếu đang chạy, đường dẫn dự phòng), và làm vài việc lặt vặt trên đường (đóng hoặc đưa cửa sổ ra trước, gửi phím / văn bản, chỉnh âm lượng…).
- ⏰ **Nhắc nhở** — bật lời nhắc đúng giờ; đọc to; lặp theo ngày trong tuần / mỗi N ngày / hằng tháng; hoặc kích hoạt "khi đăng nhập". Nhấn **Có** có thể chạy một chương trình, mở một tệp (ví dụ nhạc) hay URL, hoặc chạy một nhóm thao tác.
- 🧹 **Mục khởi động hệ thống** — liệt kê **mọi thứ tự khởi động trên máy** và tắt những gì bạn không cần (bị vô hiệu hóa, không bị xóa — bật lại bất cứ lúc nào). Một cú nhấp "tiếp quản" một mục vào danh sách khởi động của riêng bạn.
- 🎛️ **Nhóm thao tác** — gói một chuỗi thao tác thành nhóm tái sử dụng (Tập trung / Họp / Kết thúc / Trước khi ngủ…) và kích hoạt bằng một cú nhấp từ khay, danh sách khởi động, hoặc một lời nhắc. Có sẵn các mẫu dựng sẵn.

Không cần cài đặt, một thư mục di động hoàn toàn, mọi thứ cấu hình bằng chuột; giao diện tối, tương thích DPI cao.

## Yêu cầu

- Windows 10 / 11 (x64)
- Không cần cài gì: một tệp `Clockwork.exe` đơn lẻ, độc lập, đã đóng gói sẵn .NET runtime bên trong.

## Bắt đầu

1. Tải `Clockwork.exe` mới nhất từ [Releases](https://github.com/rockbenben/Clockwork/releases) và đặt vào thư mục bất kỳ (di động — để đâu cũng được). Muốn tự dựng, xem **Dành cho nhà phát triển** bên dưới.
2. Nhấp đúp **`Clockwork.exe`** để mở cửa sổ cài đặt.
   - Ở **lần chạy đầu tiên**, chương trình nạp một **cấu hình mẫu** (minh họa khởi động / nhắc nhở / nhóm thao tác) để bạn chỉnh thành của mình. Cài đặt của bạn nằm ở `clockwork.settings.json` cạnh tệp exe — chỉ trên máy, không đưa lên kho.
3. Để chạy mỗi lần khởi động máy: ở tab **Cài đặt**, nhấp **Khởi động cùng đăng nhập** (đăng ký một tác vụ theo lịch với quyền admin, để không phải chịu một loạt hộp thoại UAC khi khởi động).

> Nó nằm im trong khay hệ thống. Nhấp đúp biểu tượng khay để mở cửa sổ; nút đóng của cửa sổ chỉ thu vào khay. Muốn thoát hẳn, nhấp chuột phải vào khay và chọn **Thoát**.

## Ảnh chụp màn hình

![Ảnh chụp màn hình](../assets/screenshot.png)

## Năm tab

### Danh sách khởi động
Một **danh sách các bước có thứ tự**, chạy từ trên xuống khi đăng nhập. Nhấp **Thêm ▾** để chọn loại; thêm/xóa/sắp xếp tự do; mỗi bước có thể bật/tắt, đặt **độ trễ sau bước**, một **số lần lặp** (lặp N lần), và điều kiện (**chỉ vào một số ngày trong tuần / chỉ trước N giờ**). Các loại bước:

- **Khởi chạy chương trình** — đích (**Duyệt…** để chọn tệp) / tham số / thư mục làm việc (để trống = thư mục chứa đích) / admin. Đích có thể là `.exe`, tài liệu, lối tắt hoặc URL; `.ps1` chạy qua PowerShell. Nâng cao: **kiểu cửa sổ** (thu nhỏ / phóng to / ẩn), **kích hoạt nếu đang chạy** (đưa ra trước thay vì mở lại; tên tiến trình qua **Chọn…**), **đường dẫn dự phòng** (mỗi dòng một đường dẫn đầy đủ; dùng đường dẫn đầu tiên tồn tại — tiện khi đường dẫn cài đặt khác nhau giữa các máy).
- **Gửi phím** — ví dụ Win+D, Alt+K, Ctrl+Enter, F5 (**Bắt phím** để ghi lại tổ hợp bằng cách nhấn nó).
- **Gửi văn bản** — gõ một chuỗi vào cửa sổ đang có tiêu điểm (hoặc một **tiến trình đích** đã chọn qua **Chọn…**).
- **Âm lượng** — tắt tiếng / bật tiếng / đặt mức.
- **Thao tác cửa sổ** — theo tên tiến trình (**Chọn…**, có tìm kiếm): đóng / thu nhỏ / phóng to / đưa ra trước / đưa ra trước rồi gửi phím; ứng dụng khởi động chậm có thể **chờ cửa sổ xuất hiện tối đa N giây**.
- **Lệnh hệ thống** — hiện màn hình nền / khóa / tắt màn hình / dọn thùng rác / xóa clipboard / mở Cài đặt / Task Manager / chụp màn hình / ngủ / ngủ đông / đăng xuất / khởi động lại / tắt máy (ba mục cuối hỏi xác nhận trước).
- **Độ trễ** — chỉ chờ N giây trước bước tiếp theo.
- **Nhóm thao tác** — chạy một nhóm thao tác đã định nghĩa; đặt số lần lặp để lặp cả nhóm.

> **Độ trễ khởi động** (tab Cài đặt, chỉ khi khởi động máy): chờ một số giây cố định sau khi đăng nhập để "cơn bão đăng nhập" (tranh chấp đĩa/CPU do mọi thứ tự khởi động) đi qua trước khi danh sách chạy; chạy lại thủ công thì không bị ảnh hưởng. Tăng lên (0–600 giây) nếu mọi thứ khởi động quá sớm.

> **Dừng bất cứ lúc nào** — khay → **Dừng các thao tác đang chạy**, hoặc **phím tắt khẩn** toàn cục (đặt ở tab Cài đặt; mặc định `Ctrl+Alt+Q`). Bất cứ thứ gì đang chạy sẽ dừng sau thao tác hiện tại; các khoảng chờ dài (độ trễ khởi động, chờ cửa sổ) bị ngắt ngay lập tức.

### Nhắc nhở
Đặt một **thời điểm** (hoặc chuyển sang **khi đăng nhập**), một **chu kỳ** (ngày trong tuần / mỗi N ngày / hằng tháng), và **nội dung**; tùy chọn đọc to. Lời nhắc có thao tác **Khi-Có** (chạy chương trình / mở tệp / URL / chạy nhóm thao tác) sẽ bật hộp thoại **Có / Không** kèm nút **Hoãn** (mặc định 10 phút, menu ▾ 5–60 phút); những lời nhắc còn lại trượt vào góc dưới dạng **thẻ nhắc** (tự đóng sau số giây đã đặt, **0 = ở lại đến khi bạn bỏ qua**). Bạn cũng có thể đặt một **nhóm thao tác im lặng** — chạy một nhóm đúng giờ mà không bật cửa sổ.

Nâng cao: **tự đóng**, **nhắc lặp lại** (bật lại mỗi N phút cho đến một hạn), **độ trễ sau kích hoạt + dao động ngẫu nhiên**, **thời gian ân hạn** (bắt lại một lần bị lỡ do tắt máy/ngủ ngắn), **bù nếu bị lỡ** (bật lại một lần sau khi ngủ đông/tắt máy làm lỡ), và một **ngày mốc** cho chu kỳ mỗi N ngày (**Chọn ngày**). Trạng thái "đã bật hôm nay" và "hoãn đến" tồn tại qua các lần khởi động lại (`clockwork.state.json`), nên một lần hoãn vẫn giữ qua lần khởi động lại và không có gì bật hai lần.

Cần tập trung hay họp? Khay cung cấp **Tạm dừng nhắc nhở 1 / 2 / 4 giờ** (Không làm phiền): mọi thứ (kể cả nhóm im lặng) bị chặn và tự nối lại khi hết giờ.

### Mục khởi động hệ thống
Liệt kê **mọi thứ tự khởi động** (khóa Run trong registry, thư mục Startup, tác vụ theo lịch). Bỏ chọn **Bật** để tắt một mục — **bị vô hiệu hóa, không bị xóa; chọn lại để khôi phục** (có hiệu lực ngay). Mục được đánh dấu **cần admin** sẽ hỏi khởi động lại với quyền nâng cao. Các mục hệ thống / chính sách / một lần (Group-Policy Run, RunOnce, Winlogon, Active Setup) không thể bật tắt theo cách thông thường và **bị ẩn theo mặc định** — tích **Hiện mục hệ thống / chỉ đọc** để xem (mờ đi). **Tiếp quản vào danh sách khởi động** giao một mục cho Clockwork (chỉ khóa Run trong registry và mục thư mục Startup). Ô **lọc** ở trên tìm theo tên / lệnh; di chuột lên một lệnh bị cắt để đọc đầy đủ.

### Nhóm thao tác
Gói các thao tác thành một nhóm tái sử dụng. **Thêm ▾** bắt đầu một nhóm từ một **mẫu dựng sẵn** (Tập trung / Họp / Kết thúc / Trước khi ngủ / Rời đi một lát / Chụp màn hình) — chỉnh tên tiến trình rồi lưu. Một nhóm **chỉ định nghĩa thao tác**; kích hoạt theo ba cách: từ khay (**Chạy: <nhóm>**), như một **bước nhóm thao tác** trong danh sách khởi động (khi khởi động máy), hoặc từ một lời nhắc (**Khi-Có / nhóm im lặng**). Một nhóm chỉ chạy một bản tại một thời điểm; một bước **thông báo** có thể làm cổng xác nhận (trả lời **Không** sẽ hủy phần còn lại).

### Cài đặt
**Độ trễ khởi động** (0–600 giây, chỉ khi khởi động máy), **thu nhỏ vào khay khi khởi động**, **phím tắt khẩn** (nhấp vào ô rồi nhấn tổ hợp của bạn; Esc hủy, Delete xóa; mặc định `Ctrl+Alt+Q`), và **ngôn ngữ giao diện** (Tiếng Trung giản thể, English, 日本語 và 15 ngôn ngữ nữa — tổng 18; đổi ngôn ngữ sẽ khởi động lại ứng dụng để áp dụng).

## Mẹo

- **Nhấp đúp một dòng để chỉnh sửa** nó. Khi điền đường dẫn / tiến trình / lối tắt / ngày, bạn không phải gõ tay: **Duyệt…**, **Chọn…** (trình chọn tiến trình có tìm kiếm), **Bắt phím**, và **Chọn ngày**.
- Nhấp đúp `Clockwork.exe` chỉ mở cài đặt — **không** chạy ngay danh sách khởi động; dùng **Chạy lại danh sách khởi động** ở khay cho việc đó.
- **Khởi chạy nó theo cách bình thường** (nhấp đúp / khay / tác vụ theo lịch). Một số trình khởi chạy dạng sandbox / hạn chế quyền chặn các lời gọi cấp thấp, nên gửi phím / thao tác cửa sổ / kích hoạt nếu đang chạy / gửi văn bản đến tiến trình / âm lượng có thể không hoạt động (bạn sẽ nhận thông báo rõ ràng; "khởi chạy chương trình" thuần túy không bị ảnh hưởng).
- Cấu hình của bạn là `clockwork.settings.json` (chỉ trên máy). Xóa nó để đặt lại về mẫu. Trạng thái nhắc nhở là `clockwork.state.json` (cũng chỉ trên máy; xóa được).
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
- **CI / phát hành** (GitHub Actions): push / PR sẽ dựng và chạy toàn bộ kiểm thử trên Windows runner; đẩy một thẻ `v*` (ví dụ `v2.0.0`) sẽ dựng, đóng dấu phiên bản tệp từ thẻ, tạo một GitHub Release và đính kèm `Clockwork.exe`.

## Về 365 Open-Source Plan

Đây là dự án #20 của [365 Open-Source Plan](https://github.com/rockbenben/365opensource) — một người + AI, 300+ dự án mã nguồn mở trong một năm. [Gửi yêu cầu →](https://my.feishu.cn/share/base/form/shrcnI6y7rrmlSjbzkYXh6sjmzb)

## Giấy phép

[MIT](../LICENSE) © rockbenben
