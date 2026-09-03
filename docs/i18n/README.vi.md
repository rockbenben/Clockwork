<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Để những việc lặp đi lặp lại trên máy tính tự chạy**

Tự mở ứng dụng khi đăng nhập · nhắc nhở đúng giờ · một cú nhấp chạy cả chuỗi thao tác

**[⬇ Tải về cho Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — di động, không cần cài đặt

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../../LICENSE) [![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-3466b2)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · [Español](README.es.md) · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · **Tiếng Việt** · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

![Danh sách khởi động của Clockwork — một chuỗi các bước đăng nhập có thứ tự, mỗi bước có loại, độ trễ và điều kiện riêng](../../assets/screenshot.png)

## Nó làm được gì

- 🚀 **Danh sách khởi động** — mở lần lượt các ứng dụng thường dùng khi đăng nhập, mỗi bước có độ trễ, điều kiện ngày trong tuần và kiểu cửa sổ riêng; tiện thể đóng, đưa ra trước hoặc tắt tiếng. Các bước còn có thể phụ thuộc vào trạng thái máy: chỉ khi một ứng dụng đang chạy (hoặc không chạy), chỉ khi cắm sạc hoặc chỉ khi dùng pin, chỉ khi một tệp hay thư mục tồn tại.
- ⏰ **Tác vụ theo lịch** — một lời nhắc đúng giờ, đọc to nếu bạn muốn, hoặc một nhóm thao tác chạy im lặng. Nhấn **Có** có thể chạy chương trình, mở tệp hay URL, hoặc kích hoạt một nhóm. Hoặc để một sự kiện kích hoạt thay cho đồng hồ — khi mở khóa, khi khóa máy, khi thức khỏi chế độ ngủ, sau N phút không dùng máy, khi cắm hay rút sạc, hoặc khi pin yếu. Chỉ cần một lần thôi? Khay hệ thống có **nhắc nhanh** — từ 5 đến 60 phút, báo một lần rồi tự xóa. Vài trình kích hoạt khác trông chừng phần cứng — màn hình thay đổi, mạng trở lại hoặc rớt, cắm ổ USB — và một cái trông chừng chính bạn: nhắc sau N phút làm việc liên tục để bạn nhớ đứng dậy.
- 🎛️ **Nhóm thao tác** — gói một chuỗi việc quen thuộc (Tập trung / Họp / Kết thúc / Trước khi ngủ…) và kích hoạt từ khay, một **phím tắt toàn cục**, danh sách khởi động hoặc một tác vụ theo lịch. Có sẵn mẫu dựng sẵn. Các bước có thể chuyền giá trị cho nhau: **nhập từ người dùng, người dùng chọn** và **lấy văn bản đang chọn** đều lưu kết quả vào một biến, rồi các bước sau gọi lại nó dưới dạng `{tên}` trong một URL hay một đoạn văn bản — «hỏi tôi muốn tìm gì rồi đi tìm» chỉ là hai bước.
- 🧹 **Mục khởi động hệ thống** — mọi thứ tự khởi động trên máy gom vào một danh sách: tắt những gì bạn không cần (vô hiệu hóa chứ không xóa) hoặc tiếp quản vào danh sách khởi động của riêng bạn.
- 🔌 **Cổng** — mọi cổng TCP đang lắng nghe, cùng tiến trình đang giữ nó và dự án nơi nó sinh ra: nhấp đúp một dòng để mở `localhost:3000` trong trình duyệt, nhấp chuột phải để giải phóng cổng, kết thúc mọi tiến trình đang giữ nó cùng các tiến trình con. Mặc định chỉ hiện các dịch vụ khởi chạy từ thư mục dự án của bạn, nên dev server bạn quên tắt không bị vùi dưới các ứng dụng chat và dịch vụ hệ thống.
- ⚡ **Bảng nhanh** — một phím tắt (mặc định `Ctrl+Alt+Space`) bật lưới ô vuông ngay chỗ con trỏ đang ở: các tác vụ của bạn, thêm chạy lại danh sách, dừng, không làm phiền và mở cửa sổ. Bấm vào một ô, hoặc dùng phím mũi tên chọn rồi nhấn Enter; **Esc** hay bấm ra ngoài là đóng. Menu khay hệ thống cũng có, nên xoá phím tắt chỉ tắt phím tắt chứ không tắt tính năng. Thích dùng chuột hơn? Bật **giữ nút giữa** là mở được mà không cần bàn phím — nhấp chuột giữa như thường vẫn dùng được. Các trang của bảng là do bạn tự tạo và tự sắp trong trình quản lý bảng, và mỗi ô trên một trang là một thao tác đơn: khóa màn hình, tắt tiếng, mở ứng dụng, mở URL và «chạy cả một nhóm hành động» nhờ vậy nằm cạnh nhau. Khi trang nhiều lên thì phân loại: **hàng trên chọn một phân loại, cột trái liệt kê các trang của phân loại đó**, cả hai đều kéo thả để sắp xếp; không phân loại gì thì tất cả nằm trong *Chưa phân loại* và hàng phân loại không hề xuất hiện. Không dùng hẳn? **Cài đặt → Dùng bảng nhanh** tắt chính tính năng đó, cả phím tắt và nút giữa.
- 🖱️ **Cử chỉ chuột** — giữ **nút phải**, vẽ một nét, hành động tương ứng sẽ chạy. Tám hướng và bất kỳ loại bước nào (kể cả chạy cả một nhóm hành động). Nét vẽ hiện trên màn hình trong lúc bạn vẽ và biến mất khi thả nút. **Cài xong là đã có sẵn 10 cử chỉ** (sao chép / dán / lùi / tiến / tìm văn bản đã chọn (↑↓) / xuống dưới cùng, cùng bốn đường chéo cho thu nhỏ, phóng to, ghim lên trên và đóng cửa sổ) — dùng luôn hoặc sửa lại tùy ý. Cái giá, nói trước: chỉ cần một cử chỉ đang bật là nút phải bị chiếm, nên **kéo thả bằng nút phải phải dừng một nhịp đã** — nhấn xuống, giữ yên khoảng 0,2 giây rồi mới kéo (kéo tệp bằng nút phải trong File Explorer, xoay góc nhìn trong ứng dụng 3D). Nếu muốn nút phải hoàn toàn không bị đụng tới, hàng tiêu đề của trình quản lý cử chỉ có một công tắc tổng. Bạn cũng không nhất thiết phải dùng những cử chỉ đi kèm: tắt công tắc đó, để WGestures / StrokesPlus / Quicker lo phần vẽ, còn phần hành động vẫn ở đây qua `Clockwork.exe --run-group "Tập trung"`. Công tắc đó cũng nạm ở thẻ **Cài đặt**: **Dùng cử chỉ chuột**.

> **Dừng bất cứ lúc nào** — nút dừng ở cuối thanh thẻ (chỉ hiện khi có thứ gì đang chạy), khay hệ thống → **Dừng các hành động đang chạy**, hoặc phím dừng khẩn toàn cục (mặc định `Ctrl+Alt+Q`). Các khoảng chờ dài bị cắt ngắn chứ không phải ngồi đợi.

## Yêu cầu

| Hạng mục | Chi tiết |
| --- | --- |
| **Hệ thống** | Windows 10 / 11, x64 |
| **Cài đặt** | Không cần. Một tệp `Clockwork.exe` di động duy nhất — để vào thư mục bất kỳ |
| **Quyền admin** | Chỉ cần cho «Khởi động cùng đăng nhập» và các bước bạn đánh dấu **chạy với quyền admin** |
| **Cấu hình của bạn** | `clockwork.settings.json` cạnh tệp exe (hoặc `%APPDATA%\Clockwork\` nếu ở lần chạy đầu thư mục đó chỉ đọc; sau đó nó ở nguyên chỗ đã chọn) — không có gì rời khỏi máy |
| **Giao diện** | 18 ngôn ngữ và giao diện sáng / tối. Ngôn ngữ theo Windows ở lần chạy đầu; giao diện mặc định tối, có thể đặt theo Windows |

**Giới hạn.** Không cài đặt cũng có nghĩa là không tự cập nhật — tải zip mới và thay tệp exe. Trình khởi chạy dạng sandbox chặn gửi phím, thao tác chuột, thao tác cửa sổ, kích hoạt-nếu-đang-chạy và âm lượng (bạn sẽ nhận thông báo rõ ràng; «chạy chương trình» thuần túy vẫn hoạt động). Gán lại phím và mở rộng văn bản nằm ngoài phạm vi — đó là việc của AutoHotkey.

## Bắt đầu

1. Tải bản mới nhất từ [Releases](https://github.com/rockbenben/Clockwork/releases) — hai bản dựng, ba lượt tải — rồi đặt tệp `Clockwork.exe` duy nhất bạn có vào thư mục bất kỳ.
   - **`Clockwork-<phiên bản>-win-x64.zip`** — đã kèm .NET runtime, chạy được ngay trên mọi máy Windows 10/11. Phân vân, hoặc máy offline hay bị khóa không cài được gì, thì chọn gói này.
   - **`Clockwork-<phiên bản>-win-x64-needs-dotnet10.zip`** — cần đã cài [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Cài một lần trên máy có mạng, về sau mỗi bản cập nhật chỉ tải một gói rất nhỏ.
   - **`Clockwork.exe`** — cùng bản dựng với gói zip ngay trên, chỉ là không bọc zip: bấm vào chạy luôn, hoặc ghi đè lên bản đang dùng để cập nhật. Thiếu runtime thì Windows sẽ tự mời bạn tải.
2. Nhấp đúp để mở cửa sổ cài đặt. Các mẫu được nạp đều **chưa được tick** — không có gì chạy cho tới khi bạn tự tick.
3. Để chạy mỗi lần khởi động máy: ở tab **Cài đặt**, tick **Khởi động cùng đăng nhập** (đăng ký một tác vụ theo lịch với quyền admin, để không phải chịu một loạt hộp thoại UAC khi khởi động).

Sau đó nó nằm trong khay hệ thống: nhấp đúp biểu tượng để mở cửa sổ, còn nút đóng chỉ thu nó lại. Muốn thoát hẳn, nhấp chuột phải vào khay và chọn **Thoát**.

> [!IMPORTANT]
> **Tệp exe không được ký số**, nên ở lần chạy đầu SmartScreen hiện «Windows đã bảo vệ PC của bạn» — bấm **More info → Run anyway**. Phần mềm diệt virus cũng có thể báo: ghi khoá Run trong registry và tác vụ theo lịch đúng là việc mà một trình quản lý khởi động phải làm — và cũng là việc mà mã độc hay làm; từ bên ngoài không phân biệt được. Nếu không muốn chấp nhận bằng niềm tin, hãy [tự build lấy](../../CONTRIBUTING.md) — kết quả như nhau, tệp nhị phân là của bạn. Mỗi bản phát hành cũng kèm `SHA256SUMS.txt` và chứng thực bản dựng của GitHub: `gh attestation verify <tệp> -R rockbenben/Clockwork` chứng minh tệp tải về được dựng bởi CI của kho này, chứ không phải trên laptop của ai đó.

**Hướng dẫn đầy đủ** — từng trường, từng trường hợp biên: [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Mẹo

- **Nhấp đúp một dòng để chỉnh sửa** nó. Đường dẫn, tiến trình và ngày không phải gõ tay: **nút … ở cuối dòng** mở bộ chọn tương ứng (tệp, danh sách tiến trình có tìm kiếm, ngày), còn phím tắt thì bấm **Bắt phím** rồi nhấn trực tiếp.
- **Kéo một dòng để đổi thứ tự** — trong cả ba danh sách và trong danh sách bước của trình chỉnh sửa nhóm; các nút lên/xuống vẫn dùng được.
- **Thử trước khi lưu** — **▶ Chạy bước này** và **▶ Chạy nhóm** trong trình chỉnh sửa nhóm chạy đúng những gì đang có trên màn hình, và nút đổi thành **■ Dừng** trong lúc chạy.
- **Nhân bản** tạo bản sao của tác vụ hay nhóm đang chọn ngay bên dưới — nhanh hơn dựng lại một mục gần giống. **Xóa luôn hỏi xác nhận trước**, ở mọi nơi.
- Nhấp đúp `Clockwork.exe` chỉ mở cửa sổ; nó **không** chạy lại danh sách khởi động. Dùng **Chạy lại danh sách khởi động** ở khay cho việc đó.

## Giới thiệu về 365 Open Source Plan

Dự án **#020** của [365 Open Source Plan](https://github.com/rockbenben/365opensource) — một người + AI, hơn 300 dự án mã nguồn mở trong một năm.

[Gửi ý tưởng của bạn →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
