# QuadPane

Trình quản lý file 4 panel viết bằng C# + **Avalonia 12** (.NET 10), có giao diện Fluent hiện đại với chế độ Sáng / Tối / Theo hệ thống. Đây là dự án học tập, tự viết lại dựa trên kiến trúc rút ra khi phân tích Q-Dir; không dùng code hay tài nguyên của Q-Dir.

## Chạy

```powershell
dotnet run
```

Cần Windows 10/11, vì mỗi panel nhúng Explorer Browser của Windows.

Để dùng tìm kiếm, cài và chạy [Everything](https://www.voidtools.com/). App đóng gói sẵn DLL của Everything SDK cho x86, x64 và ARM64; DLL gửi truy vấn tới Everything đang chạy, không tự tạo chỉ mục.

## Tạo GitHub Release

Workflow [`.github/workflows/release.yml`](../.github/workflows/release.yml) chạy mỗi khi có push lên `main`, gồm cả commit được merge vào nhánh này. Sau khi build thành công, workflow tự tạo tag và GitHub Release dạng `v0.0.N`, với `N` là số lần chạy workflow. Tag trỏ tới commit vừa được build; chạy lại cùng một workflow sẽ cập nhật các gói của release đó.

Mỗi release có bộ cài `QuadPane-v0.0.N-win-x64-setup.exe`, gói ZIP tự chứa .NET cho Windows x64, ARM64 và x86, cùng file `SHA256SUMS.txt`. Bộ cài đặt mặc định vào `%LocalAppData%\Programs\QuadPane` và tạo lối tắt trong Start Menu. Với gói ZIP, tải đúng kiến trúc, giải nén vào thư mục riêng rồi chạy `QuadPane.exe`; không cần cài .NET Runtime.

Bản Release kiểm tra `github.com/.../releases/latest` khi khởi động và mỗi 2 phút, dùng proxy hệ thống, xác thực proxy Windows và chứng chỉ trong kho tin cậy của Windows. Khi có version mới, nút cập nhật hiện trên thanh trạng thái; bấm nút để xác nhận tải và cài. Nếu kiểm tra thất bại, nút lỗi hiện nguyên nhân trong tooltip và cho phép thử lại; lỗi cũng được ghi vào `%TEMP%\QuadPane-crash.log`. Gói ZIP được kiểm tra SHA-256, cài sau khi app thoát rồi tự mở lại. Cần quyền ghi vào thư mục cài đặt. Bản cũ được giữ trong thư mục `.QuadPane-backup-*` cạnh thư mục ứng dụng để có thể khôi phục thủ công nếu cần. Bản Debug không tự cập nhật.

Commit `Assets/EverythingSdk` (cả ba DLL và `LICENSE.txt`) cùng mã nguồn trước khi push lên `main`. Thiếu file SDK sẽ làm workflow thất bại. GitHub Actions cần được bật cho repository và cho phép `GITHUB_TOKEN` tạo release (workflow đã khai báo `contents: write`).

## Giao diện

- Thanh tiêu đề tự vẽ (`ExtendClientAreaToDecorationsHint`) chứa logo, tên app, nút chọn bố cục 4/3/2/1 khung, nút chia đều và nút đổi theme.
- Nền Mica trên Windows 11; các máy không hỗ trợ dùng màu nền thay thế.
- Panel đang hoạt động có viền màu nhấn (accent).
- Mỗi panel có dải tab riêng. Tab mới mở tại `C:\`. Mỗi tab giữ Explorer Browser và lịch sử điều hướng riêng trong phiên chạy; các thư mục và tab đang chọn được khôi phục ở lần mở sau.
- Mỗi tab có nút Pin để ghim thư mục đang xem làm home riêng. Nút Home chỉ bật khi tab đã ghim; nhấn để quay lại thư mục đó. Ghim được lưu cùng layout, và nhấn Pin lần nữa tại thư mục đã ghim để bỏ ghim.
- Mở `QuadPane.exe "C:\duong-dan\thu-muc"` để chuyển tới thư mục. Có thể truyền đường dẫn file đang tồn tại để mở thư mục chứa file. Nếu thư mục đã ở một tab, QuadPane chọn tab đó; ưu tiên panel có số nhỏ hơn, rồi tab đang chọn trong panel. Nếu chưa có, QuadPane tạo tab tại panel 1. Lệnh gửi tới cửa sổ đang chạy và đưa cửa sổ lên trước.
- Nút đóng cửa sổ ẩn QuadPane xuống khay hệ thống. Nhấp icon khay hoặc mở QuadPane lần nữa để hiện cửa sổ cũ; chọn **Thoát** trong menu icon khay để đóng hẳn app.
- Góc phải dải tab mở Claude Code, Codex, VS Code tại thư mục đang xem hoặc sao chép đường dẫn mục đang chọn (nhiều mục: mỗi đường dẫn một dòng; không chọn: thư mục đang mở). Claude/Codex mở trong Windows Terminal nếu có, nếu không dùng PowerShell. Cần cài các công cụ CLI `claude`, `codex`, `code` trong PATH. Cài đặt ở góc trái thanh trạng thái có tuỳ chọn khởi động cùng Windows cho tài khoản hiện tại.
- Thanh trạng thái hiển thị số mục trong tab; khi thư mục thuộc một Git repository và có `git` trong PATH, thanh hiện branch, số file đã stage/chưa stage, cùng nút Fetch, Pull (`--ff-only`) và Push. Nút Git log chỉ hiện khi có TortoiseGit và mở lịch sử trong TortoiseGit. Không tự tạo merge commit.
- Ô Everything nằm trên thanh tiêu đề. Tab kết quả giữ panel ban đầu khi đổi panel/tab rồi tiếp tục gõ; QuadPane chọn lại tab đó. Danh sách dùng font hệ thống và các cột Name, Date modified, Type, Size; tooltip của mục hiện đầy đủ đường dẫn. Enter/↓ chuyển tới danh sách; trong danh sách, Enter hoặc nhấp đúp mở mục. Tab tìm kiếm chỉ tồn tại trong phiên chạy.
- Theme đổi theo vòng: **Theo hệ thống → Sáng → Tối**. Phần Explorer nhúng (danh sách file, header cột, cây thư mục, menu chuột phải) và khung cửa sổ đổi màu theo.

## Logo & icon

- `Assets/logo.svg`: bản thiết kế gốc. Bốn ô panel tạo thành chữ Q, với ô đang chọn và nét đuôi màu xanh cyan.
- `Assets/QuadPane.ico`: icon cho exe, cửa sổ và taskbar, gồm các cỡ 16–256 px.
- `Assets/logo-256.png`: bản PNG của logo.
- Logo trên header dùng cùng ảnh PNG, ở kích thước 20 px.
- Icon Claude và OpenAI dùng hình từ Simple Icons; icon VS Code dùng logo từ Wikimedia Commons. Tên công cụ vẫn hiện trong tooltip của từng nút.
- Sau khi sửa thiết kế, tạo lại icon bằng lệnh `dotnet run --file tools/make-icon.cs`. Lệnh này cũng xuất `tools/icon-preview.png` để xem trước mọi kích thước.

## Kiến trúc

| File | Vai trò | Tương ứng trong Q-Dir |
|---|---|---|
| `Shell/NativeMethods.cs` | Khai báo COM: `IExplorerBrowser`, `IExplorerBrowserEvents`, `IInputObject`, `IShellView` | Q-Dir tự cài `IShellBrowser` + `ICommDlgBrowser` để host `IShellView` |
| `Shell/ExplorerHost.cs` | `NativeControlHost` nhúng Explorer Browser; lớp cửa sổ Win32 riêng nhận `WM_SIZE` | `SHELLDLL_DefView` → `SysListView32` trong từng panel |
| `Shell/ShellTheme.cs` | Dark/light cho Explorer nhúng và khung cửa sổ | `color.ini`, khoá `colorthema` |
| `PaneView.axaml` | Dải tab, thanh điều hướng + ô địa chỉ + các Explorer Browser | Thanh địa chỉ của mỗi panel |
| `MainWindow.axaml` | Header, lưới 2×2 với 2 splitter cắt nhau, chế độ 4/3/2/1 khung, phím tắt | `WTL_XSplitterWindow`, khoá `SP0/SP1/SPT` |
| `LayoutStore.cs` | Lưu bố cục và theme vào `%AppData%\QuadPane\layout.json` | File `.qdr` dạng INI |

## Những điểm cần lưu ý

- **Bàn phím:** focus nằm trong cửa sổ Win32 của Explorer nên Avalonia không thấy phím. `MainWindow` cài hook `WH_GETMESSAGE` trên thread UI rồi xử lý theo thứ tự:
  1. phím tắt chung;
  2. `IInputObject.TranslateAcceleratorIO` (Delete, F2, Ctrl+C/V…).

  Message đã xử lý được đổi thành `WM_NULL`.
- **Dark mode của Explorer:** `SetPreferredAppMode`, `AllowDarkModeForWindow` và `FlushMenuThemes` là API **không công bố** của `uxtheme.dll` (gọi theo số thứ tự). Mọi lời gọi được bọc try/catch. Danh sách file được tô lại sau mỗi lần điều hướng, vì Explorer tạo list view mới cho mỗi thư mục.
- **Tiêu đề do Avalonia vẽ:** Avalonia 12 tự vẽ chữ tiêu đề trong lớp overlay của `TopLevelHost`, nằm ngoài cây visual của Window nên style không ghi đè được. `MainWindow.HideDrawnTitle()` ẩn nó bằng code.
- **Không điều hướng trong `CreateNativeControlCore`:** bước này được dời bằng `Dispatcher.UIThread.Post`.
- **Smart App Control:** các DLL của Avalonia không có chữ ký số. Khi Smart App Control bật, Windows có thể chặn `Avalonia.Vulkan.dll` hoặc chính `QuadPane.exe`.
- **Telemetry khi build:** gói `Avalonia.BuildServices` bị loại trong `.csproj`, vì task telemetry của nó không nạp được với MSBuild hiện tại.

## Phím tắt

- `Ctrl+1..4`: chọn panel
- `Ctrl+T` / `Ctrl+W`: mở / đóng tab trong panel đang hoạt động
- `Ctrl+Tab` / `Ctrl+Shift+Tab`: chuyển tab kế tiếp / trước đó
- `Ctrl+L` / `Alt+D`: ô địa chỉ
- `Ctrl+F`: chuyển tới ô tìm kiếm Everything trên thanh tiêu đề. Nhập từ khóa để thấy tối đa 200 kết quả đầu tiên trong tab mới tại panel đang chọn.
- `Alt+←/→/↑`: lùi / tiến / lên thư mục cha
- `F5`: làm mới

## Hướng mở rộng

- Lọc file
- Tô màu theo phần mở rộng
- Danh sách thư mục yêu thích
- Khung xem trước
