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
- Panel đang hoạt động có quầng sáng nhẹ quanh viền.
- Mỗi panel có dải tab riêng. Tab mới mở tại `C:\`. Mỗi tab giữ Explorer Browser và lịch sử điều hướng riêng trong phiên chạy; các thư mục và tab đang chọn được khôi phục ở lần mở sau.
- Mỗi tab có nút Pin để ghim thư mục đang xem làm home riêng. Nút Home chỉ bật khi tab đã ghim; nhấn để quay lại thư mục đó. Ghim được lưu cùng layout, và nhấn Pin lần nữa tại thư mục đã ghim để bỏ ghim.
- Mở `QuadPane.exe "C:\duong-dan\thu-muc"` để chuyển tới thư mục. Có thể truyền đường dẫn file đang tồn tại để mở thư mục chứa file. Nếu thư mục đã ở một tab, QuadPane chọn tab đó; ưu tiên panel có số nhỏ hơn, rồi tab đang chọn trong panel. Nếu chưa có, QuadPane tạo tab tại panel 1. Lệnh gửi tới cửa sổ đang chạy và đưa cửa sổ lên trước.
- Nút đóng cửa sổ mặc định ẩn QuadPane xuống khay hệ thống (đổi thành thoát hẳn trong Cài đặt). Nhấp icon khay hoặc mở QuadPane lần nữa để hiện cửa sổ cũ; chọn **Thoát** trong menu icon khay để đóng hẳn app.
- Góc phải dải tab mở Claude Code, Codex, VS Code tại thư mục đang xem hoặc sao chép đường dẫn mục đang chọn (nhiều mục: mỗi đường dẫn một dòng; không chọn: thư mục đang mở). Claude/Codex mở trong Windows Terminal nếu có, nếu không dùng PowerShell. Cần cài các công cụ CLI `claude`, `codex`, `code` trong PATH.
- Thanh trạng thái hiển thị số mục trong tab, hoặc tên và dung lượng mục/thư mục đang chọn; khi thư mục thuộc một Git repository và có `git` trong PATH, thanh hiện branch, số file đã stage/chưa stage, cùng nút Fetch, Pull (`--ff-only`) và Push. Nút Git log và đổi branch chỉ hiện khi có TortoiseGit; chúng mở hộp thoại tương ứng trong TortoiseGit. Không tự tạo merge commit.
- Ô Everything nằm trên thanh tiêu đề. Tab kết quả giữ panel ban đầu khi đổi panel/tab rồi tiếp tục gõ; QuadPane chọn lại tab đó. Danh sách dùng font hệ thống và các cột Name, Date modified, Type, Size (cột Size hiển thị cả dung lượng thư mục khi Everything bật index folder size); tooltip của mục hiện đầy đủ đường dẫn. Enter hoặc nhấp đúp mở mục, Ctrl+Enter hoặc nút "Mở vị trí" để mở thư mục chứa tệp trong tab mới. Tab tìm kiếm chỉ tồn tại trong phiên chạy.
- Kết quả Everything hiển thị icon file từ Windows Shell. Nhấp phải vào mục để mở context menu (mở file, mở vị trí trong QuadPane/Windows Explorer, sao chép đường dẫn hoặc mở menu ngữ cảnh Windows).
- Nút bánh răng ở góc trái thanh trạng thái (hoặc `Ctrl+,`) mở cửa sổ **Cài đặt** kiểu Windows 11. Mọi thay đổi áp dụng và lưu ngay vào `layout.json`:
  - **Chung:** khởi động cùng Windows, hành vi nút đóng (thu xuống khay / thoát), thư mục cho tab mới.
  - **Giao diện:** chủ đề, bật/tắt thông tin Git trên thanh trạng thái.
  - **Công cụ ngoài:** ẩn/hiện nút Claude Code, Codex, VS Code (kèm trạng thái lệnh có trong PATH hay không), chọn mở bằng Windows Terminal hay PowerShell.
  - **Tìm kiếm:** trạng thái Everything, bật/tắt hiển thị dung lượng thư mục và liên kết tải.
  - **Phím tắt**, **Cập nhật** (kiểm tra ngay, bật/tắt tự kiểm tra), **Giới thiệu** (mở thư mục cấu hình, nhật ký lỗi, GitHub).
- **Tab:** nhấp phải vào tab để ghim, nhân bản, chuyển trái/phải, chuyển sang panel khác, đóng các tab khác hoặc các tab bên phải. Kéo tab trong dải tab để sắp xếp lại thứ tự (vạch màu chỉ chỗ thả); kéo thả vào panel khác để chuyển tab sang đó (tab mới mở lại thư mục, lịch sử lùi/tiến không đi theo).
- **Ghim tab:** tab ghim có biểu tượng 📌, luôn nằm đầu dải tab, không có nút × và không đóng bằng chuột giữa, `Ctrl+W`, "Đóng các tab khác" hay "Đóng các tab bên phải" (vẫn đóng được bằng "Đóng tab" trong menu chuột phải). Trạng thái ghim được lưu cùng layout và workspace, và đi theo tab khi chuyển sang panel khác. (Khác với nút Pin trên thanh điều hướng, dùng để ghim thư mục home.) `Ctrl+Shift+T` mở lại tab vừa đóng (nhớ 25 tab trong phiên chạy).
- **Yêu thích & gần đây:** nút ☆ trên thanh điều hướng (hoặc `Ctrl+B`) mở menu gồm thư mục yêu thích, 20 thư mục gần đây và các tab đã đóng. `Ctrl+Shift+B` thêm / bỏ thư mục đang xem. Sắp xếp và xoá trong Cài đặt → Yêu thích.
- **Sao chép / di chuyển sang panel kia:** `Shift+F5` / `Shift+F6` dùng hộp thoại tiến trình của Windows (hỏi khi trùng tên, hoàn tác được bằng Ctrl+Z trong Explorer). Panel đích là panel hoạt động ngay trước đó nếu đang hiện, không thì panel cùng hàng.
- **Lọc nhanh:** ô "Lọc..." trên thanh điều hướng (`Ctrl+E`) chỉ hiện các mục có tên chứa chuỗi đã gõ, dùng `IFolderFilterSite` của Explorer Browser. Mỗi tab giữ bộ lọc riêng; bộ lọc tự xoá khi chuyển thư mục; `Esc` để xoá.
- **Khung xem trước:** nút cạnh nút cây thư mục (hoặc `Alt+P`) bật preview pane của Windows cho panel đó; trạng thái được lưu.
- **Terminal:** nút trên dải tab (hoặc ``Ctrl+` ``) mở Windows Terminal, hoặc PowerShell nếu không có, tại thư mục đang xem.
- **Workspace:** nút trên thanh tiêu đề lưu và chuyển nhanh giữa các bộ tab + thư mục home + bố cục có tên (`Ctrl+Shift+S` để lưu). Đổi tên / xoá trong Cài đặt → Workspace.
- **Bảng lệnh:** `Ctrl+Shift+P` (hoặc nút trên thanh tiêu đề) để tìm và chạy mọi lệnh, workspace, thư mục yêu thích / gần đây. Tìm không dấu: gõ "mo lai" sẽ ra "Mở lại tab vừa đóng".
- **Phím tắt tuỳ chỉnh:** Cài đặt → Phím tắt cho đổi, bỏ gán hoặc khôi phục mặc định từng lệnh. Gán phím đang dùng sẽ gỡ phím đó khỏi lệnh cũ. Danh mục lệnh nằm trong `Commands.cs`.
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
| `SettingsWindow.axaml` | Cửa sổ Cài đặt: thanh mục bên trái, thẻ tuỳ chọn bên phải | Hộp thoại Options |
| `Commands.cs` | Danh mục lệnh, phím tắt mặc định, `KeyChord` (đổi virtual-key ↔ `Key`) | — |
| `CommandPalette.cs` / `InputDialog.cs` | Bảng lệnh và hộp thoại nhập tên | — |
| `LayoutStore.cs` | Lưu bố cục, theme, cài đặt, yêu thích, workspace vào `%AppData%\QuadPane\layout.json` | File `.qdr` dạng INI |

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

Phím mặc định; đổi trong Cài đặt → Phím tắt.

- `Ctrl+1..4`: chọn panel
- `Ctrl+T` / `Ctrl+W`: mở / đóng tab · `Ctrl+Shift+T`: mở lại tab vừa đóng · `Ctrl+Shift+D`: nhân bản tab
- `Ctrl+Tab` / `Ctrl+Shift+Tab`: chuyển tab kế tiếp / trước đó
- `Ctrl+Shift+PageUp` / `Ctrl+Shift+PageDown`: chuyển tab sang trái / phải · Ghim / bỏ ghim tab: gán phím trong Cài đặt → Phím tắt
- `Ctrl+L` / `Alt+D`: ô địa chỉ
- `Ctrl+F`: tìm với Everything · `Ctrl+E`: lọc nhanh thư mục hiện tại
- `Alt+←/→/↑`: lùi / tiến / lên thư mục cha · `F5`: làm mới
- `Ctrl+B`: yêu thích, gần đây, tab đã đóng · `Ctrl+Shift+B`: thêm / bỏ yêu thích
- `Shift+F5` / `Shift+F6`: sao chép / di chuyển mục chọn sang panel kia
- ``Ctrl+` ``: mở Terminal · `Alt+P`: khung xem trước
- `Ctrl+Shift+S`: lưu workspace · `Ctrl+Shift+P`: bảng lệnh · `Ctrl+,`: Cài đặt
- Nút Back/Forward trên chuột (kể cả khi driver đổi thành phím Browser Back/Forward hoặc WM_APPCOMMAND): lùi / tiến trong panel đang trỏ tới; bấm nhanh liên tiếp lùi/tiến đủ số bước như File Explorer

## Hướng mở rộng

- Tô màu theo phần mở rộng
- Đổi tên hàng loạt
- So sánh / đồng bộ hai thư mục
