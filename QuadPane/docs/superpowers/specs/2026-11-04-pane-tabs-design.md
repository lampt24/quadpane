# Tab riêng cho từng panel

## Mục đích

Mỗi panel của QuadPane có dải tab giống File Explorer trên Windows 11 để mở nhiều thư mục trong cùng một panel. Mỗi tab giữ lịch sử điều hướng và trạng thái danh sách file riêng khi chuyển qua lại.

## Phạm vi

- Dải tab nằm phía trên thanh điều hướng của từng panel; tab đang chọn nổi bật, tên tab lấy từ thư mục hiện tại và được cập nhật khi điều hướng.
- Nút `+` và `Ctrl+T` tạo tab mới tại `C:\`. Chọn tab bằng chuột; đóng bằng `×`, nhấp chuột giữa, hoặc `Ctrl+W`. `Ctrl+Tab` / `Ctrl+Shift+Tab` chuyển tab trong panel đang hoạt động.
- Mỗi tab sở hữu một `ExplorerHost` riêng để giữ lịch sử quay lại/tiến tới và trạng thái shell trong phiên chạy. Thanh địa chỉ và các nút điều hướng tác động lên tab đang chọn.
- Mỗi panel luôn còn ít nhất một tab. Đóng tab cuối điều hướng tab đó về thư mục mặc định của panel; nếu thư mục mặc định không khả dụng thì mở This PC.
- Lưu đường dẫn, mã ổn định của từng tab và tab đang chọn cho từng panel vào cấu hình. Khởi động lại tái tạo các tab tại thư mục đã lưu và giữ thiết lập chế độ xem Explorer theo mã tab; lịch sử điều hướng chỉ cần giữ trong phiên chạy.
- Giữ các bố cục 1–4 panel, tìm kiếm Everything, cây thư mục, theme, phím tắt hiện có.

## Cách làm

Đã cân nhắc hai cách: (1) một `ExplorerHost` cho mỗi tab, giữ lịch sử và trạng thái riêng nhưng dùng thêm tài nguyên; (2) một `ExplorerHost` cho mỗi panel, chỉ lưu đường dẫn và nhẹ hơn. Chọn cách (1) theo yêu cầu. Dùng Avalonia cho dải tab, tiếp tục nhúng Explorer Browser bằng `NativeControlHost` hiện có. Chỉ tab đang chọn được hiển thị; khi chuyển tab không hủy `ExplorerHost` của tab cũ.

## Giới hạn

Không thêm kéo thả để sắp xếp hoặc chuyển tab giữa panel, và không lưu lịch sử quay lại/tiến tới qua lần chạy ứng dụng. Các tab cũ từ cấu hình hiện tại được chuyển thành một tab cho mỗi panel.
