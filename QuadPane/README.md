# QuadPane

Trình quản lý file 4 panel viết bằng C# + **Avalonia 12** (.NET 10), có giao diện Fluent hiện đại với chế độ Sáng / Tối / Theo hệ thống. Đây là dự án học tập, tự viết lại dựa trên kiến trúc rút ra khi phân tích Q-Dir; không dùng code hay tài nguyên của Q-Dir.

## Chạy

```powershell
dotnet run
```

Cần Windows 10/11, vì mỗi panel nhúng Explorer Browser của Windows.

## Giao diện

- Thanh tiêu đề tự vẽ (`ExtendClientAreaToDecorationsHint`) chứa logo, đường dẫn đang mở, nút chọn bố cục 4/3/2/1 khung, nút chia đều và nút đổi theme.
- Nền Mica trên Windows 11; các máy không hỗ trợ dùng màu nền thay thế.
- Mỗi panel là một thẻ bo góc; panel đang hoạt động có viền màu nhấn (accent).
- Theme đổi theo vòng: **Theo hệ thống → Sáng → Tối**. Phần Explorer nhúng (danh sách file, header cột, cây thư mục, menu chuột phải) và khung cửa sổ đổi màu theo.

## Logo & icon

- `Assets/logo.svg`: bản thiết kế gốc. Nền bo góc gradient xanh → tím, bên trong là lưới 2×2 panel; ô trên-trái sáng đặc tượng trưng cho panel đang hoạt động.
- `Assets/QuadPane.ico`: icon cho exe, cửa sổ và taskbar, gồm các cỡ 16–256 px. Mỗi cỡ được dựng lại theo lưới pixel để không bị nhòe.
- `Assets/logo-256.png`: bản PNG của logo.
- Logo trên header được vẽ bằng vector trong `MainWindow.axaml`.
- Sau khi sửa thiết kế, tạo lại icon bằng lệnh `dotnet run tools/make-icon.cs`. Lệnh này cũng xuất `tools/icon-preview.png` để xem trước mọi kích thước.

## Kiến trúc

| File | Vai trò | Tương ứng trong Q-Dir |
|---|---|---|
| `Shell/NativeMethods.cs` | Khai báo COM: `IExplorerBrowser`, `IExplorerBrowserEvents`, `IInputObject`, `IShellView` | Q-Dir tự cài `IShellBrowser` + `ICommDlgBrowser` để host `IShellView` |
| `Shell/ExplorerHost.cs` | `NativeControlHost` nhúng Explorer Browser; lớp cửa sổ Win32 riêng nhận `WM_SIZE` | `SHELLDLL_DefView` → `SysListView32` trong từng panel |
| `Shell/ShellTheme.cs` | Dark/light cho Explorer nhúng và khung cửa sổ | `color.ini`, khoá `colorthema` |
| `PaneView.axaml` | Thanh điều hướng + ô địa chỉ + view | Thanh địa chỉ của mỗi panel |
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
- `Ctrl+L` / `Alt+D`: ô địa chỉ
- `Alt+←/→/↑`: lùi / tiến / lên thư mục cha
- `F5`: làm mới

## Hướng mở rộng

- Tab trong từng panel
- Lọc file
- Tô màu theo phần mở rộng
- Danh sách thư mục yêu thích
- Khung xem trước
