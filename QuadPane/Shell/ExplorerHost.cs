using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using static QuadPane.Shell.NativeMethods;

namespace QuadPane.Shell;

/// <summary>
/// Nhúng Explorer Browser của Windows (IExplorerBrowser) vào Avalonia qua NativeControlHost.
/// Q-Dir gốc tự cài đặt IShellBrowser để host IShellView; ở đây dùng IExplorerBrowser
/// (có từ Windows 7), cách hiện đại và đơn giản hơn nhiều.
/// </summary>
public sealed class ExplorerHost : NativeControlHost
{
    private const string HostClassName = "QuadPane.ExplorerHost";
    private static readonly WndProcDelegate s_wndProc = HostWndProc; // giữ delegate sống
    private static readonly Dictionary<IntPtr, ExplorerHost> s_hosts = new();
    private static bool s_classRegistered;

    private IntPtr _hostHwnd;
    private IExplorerBrowser? _browser;
    private BrowserSite? _site;
    private EventSink? _sink;
    private uint _cookie;
    private IntPtr _currentPidl;
    private bool _showTree;
    private bool _showPreview;

    // IExplorerBrowser không cho đọc travel log, nên tự đếm số bước lùi/tiến để bật/tắt nút.
    private int _backCount, _forwardCount;
    private int _pendingHistory;      // số lệnh lùi/tiến đã gửi nhưng chưa điều hướng xong
    private bool _skipNextEntry;      // điều hướng khôi phục sau khi dựng lại browser: không thêm vào lịch sử
    private NameFilter? _filter;
    private bool _dark;
    private int _itemCount;

    /// <summary>Thư mục mở đầu tiên (parsing name). Đặt trước khi control được nạp.</summary>
    public string? InitialPath { get; set; }

    /// <summary>Tên tệp/thư mục cần chọn và focus sau khi điều hướng xong.</summary>
    public string? SelectItemName { get; set; }

    /// <summary>Tên dùng để Windows lưu riêng trạng thái view (cột, kiểu xem) cho panel này.</summary>
    public string PropertyBagName { get; set; } = "QuadPane";

    /// <summary>Phát ra khi đã điều hướng xong: (tên hiển thị để sửa, parsing name để lưu).</summary>
    public event Action<string, string>? Navigated;
    public event Action? ItemCountChanged;
    public event Action? HistoryChanged;

    public bool CanGoBack => _backCount > 0;
    public bool CanGoForward => _forwardCount > 0;

    public string? CurrentParsingName => GetName(_currentPidl, SIGDN_DESKTOPABSOLUTEPARSING);

    public int ItemCount => _itemCount;

    private void UpdateItemCount()
    {
        if (_browser is null) return;
        var viewId = IID_IFolderView;
        if (_browser.GetCurrentView(ref viewId, out var pointer) != 0 || pointer == IntPtr.Zero) return;
        try
        {
            var view = (IFolderView)Marshal.GetObjectForIUnknown(pointer);
            try { if (view.ItemCount(SVGIO_ALL, out int count) == 0) _itemCount = count; }
            finally { Marshal.ReleaseComObject(view); }
        }
        catch (COMException) { }
        catch (InvalidCastException) { }
        finally { Marshal.Release(pointer); }
        ItemCountChanged?.Invoke();
        if (!string.IsNullOrEmpty(SelectItemName))
        {
            TrySelectPendingItem(1);
        }
    }

    public bool ShowTree
    {
        get => _showTree;
        set
        {
            if (_showTree == value) return;
            _showTree = value;
            // Pane visibility được đọc khi Browser khởi tạo, nên phải dựng lại.
            if (_browser != null) RecreateBrowser();
        }
    }

    public bool ShowPreview
    {
        get => _showPreview;
        set
        {
            if (_showPreview == value) return;
            _showPreview = value;
            if (_browser != null) RecreateBrowser();
        }
    }

    /// <summary>Chuỗi lọc đang áp dụng (rỗng = không lọc).</summary>
    public string FilterText => _filter?.Text ?? "";

    /// <summary>
    /// Chỉ hiện các mục có tên chứa <paramref name="text"/> (không phân biệt hoa thường).
    /// Dùng IFolderFilterSite của Explorer Browser rồi làm mới view để lọc lại.
    /// </summary>
    public bool SetFilter(string text)
    {
        text = text.Trim();
        if (text == FilterText) return true;
        _filter = text.Length == 0 ? null : new NameFilter(text);
        bool ok = ApplyFilter();
        RefreshView();
        return ok;
    }

    private bool ApplyFilter() =>
        _browser is null || (_browser is IFolderFilterSite site && site.SetFilter(_filter) >= 0);

    /// <summary>Chế độ tối cho phần Explorer bên trong.</summary>
    public bool Dark
    {
        get => _dark;
        set { _dark = value; ApplyTheme(); }
    }

    private const ExplorerBrowserOptions BrowserOptions =
        ExplorerBrowserOptions.NoBorder | ExplorerBrowserOptions.ShowFrames;

    // ---- Vòng đời cửa sổ native ----

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        EnsureClassRegistered();
        _hostHwnd = CreateWindowEx(0, HostClassName, "",
            WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (_hostHwnd == IntPtr.Zero) return base.CreateNativeControlCore(parent);
        s_hosts[_hostHwnd] = this;

        CreateBrowser();

        // Không điều hướng ngay trong lúc framework đang dựng cây giao diện:
        // shell sẽ bơm message lồng nhau. Dời sang lượt dispatcher sau.
        Dispatcher.UIThread.Post(() =>
        {
            if (!Navigate(InitialPath ?? ""))
                Navigate("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"); // This PC
        });

        return new PlatformHandle(_hostHwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        DestroyBrowser();
        if (_currentPidl != IntPtr.Zero) { ILFree(_currentPidl); _currentPidl = IntPtr.Zero; }
        s_hosts.Remove(control.Handle);
        DestroyWindow(control.Handle);
        _hostHwnd = IntPtr.Zero;
    }

    private void CreateBrowser()
    {
        var type = Type.GetTypeFromCLSID(CLSID_ExplorerBrowser, throwOnError: true)!;
        _browser = (IExplorerBrowser)Activator.CreateInstance(type)!;
        _site = new BrowserSite(this);
        Marshal.ThrowExceptionForHR(((IObjectWithSite)_browser).SetSite(_site));
        Marshal.ThrowExceptionForHR(_browser.SetOptions(BrowserOptions));
        if (!GetClientRect(_hostHwnd, out var rc)) rc = new RECT(0, 0, 1, 1);
        var fs = new FOLDERSETTINGS { ViewMode = FVM_DETAILS, fFlags = 0 };
        Marshal.ThrowExceptionForHR(_browser.Initialize(_hostHwnd, ref rc, ref fs));
        Marshal.ThrowExceptionForHR(_browser.SetPropertyBag(PropertyBagName));
        _sink = new EventSink(this);
        Marshal.ThrowExceptionForHR(_browser.Advise(_sink, out _cookie));
        ApplyFilter();
    }

    private void DestroyBrowser()
    {
        if (_browser == null) return;
        if (_cookie != 0) _browser.Unadvise(_cookie);
        _cookie = 0;
        _sink = null;
        ((IObjectWithSite)_browser).SetSite(null);
        _browser.Destroy();
        Marshal.ReleaseComObject(_browser);
        _browser = null;
        _site = null;
    }

    private void RecreateBrowser()
    {
        // BrowseToIDList có thể phát OnNavigationComplete đồng bộ; giữ PIDL riêng
        // để callback không giải phóng PIDL đang được lệnh Browse sử dụng.
        IntPtr restorePidl = _currentPidl != IntPtr.Zero ? ILClone(_currentPidl) : IntPtr.Zero;
        try
        {
            DestroyBrowser();
            CreateBrowser();
            // Browser mới có travel log trống
            _backCount = _forwardCount = _pendingHistory = 0;
            _skipNextEntry = restorePidl != IntPtr.Zero;
            HistoryChanged?.Invoke();
            if (restorePidl != IntPtr.Zero)
                Marshal.ThrowExceptionForHR(_browser!.BrowseToIDList(restorePidl, SBSP_ABSOLUTE));
        }
        finally
        {
            if (restorePidl != IntPtr.Zero) ILFree(restorePidl);
        }
    }

    private static void EnsureClassRegistered()
    {
        if (s_classRegistered) return;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            hInstance = GetModuleHandle(null),
            hCursor = LoadCursor(IntPtr.Zero, 32512), // IDC_ARROW
            lpszClassName = HostClassName,
        };
        RegisterClassEx(ref wc);
        s_classRegistered = true;
    }

    private static IntPtr HostWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_SIZE && s_hosts.TryGetValue(hwnd, out var host) && host._browser != null)
        {
            long lp = lParam.ToInt64();
            int w = (int)(lp & 0xFFFF), h = (int)((lp >> 16) & 0xFFFF);
            host._browser.SetRect(IntPtr.Zero, new RECT(0, 0, w, h));
            return IntPtr.Zero;
        }
        if (msg == WM_ERASEBKGND) return (IntPtr)1; // Explorer phủ kín, tránh nháy trắng
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    // ---- Điều hướng ----

    public bool Navigate(string path, string? selectItem = null)
    {
        if (!string.IsNullOrEmpty(selectItem)) SelectItemName = selectItem;
        if (_browser == null || string.IsNullOrWhiteSpace(path)) return false;
        var pidl = ParsePath(Environment.ExpandEnvironmentVariables(path.Trim()));
        if (pidl == IntPtr.Zero) return false;
        try { return _browser.BrowseToIDList(pidl, SBSP_ABSOLUTE) >= 0; }
        finally { ILFree(pidl); }
    }

    public void GoBack() => StepHistory(back: true);
    public void GoForward() => StepHistory(back: false);

    private void StepHistory(bool back)
    {
        if (_browser == null || (back ? _backCount : _forwardCount) == 0) return;
        int step = back ? 1 : -1;
        // Cập nhật trước khi gọi: BrowseToIDList có thể phát OnNavigationComplete đồng bộ
        _backCount -= step;
        _forwardCount += step;
        _pendingHistory++;
        if (_browser.BrowseToIDList(IntPtr.Zero, back ? SBSP_NAVIGATEBACK : SBSP_NAVIGATEFORWARD) < 0)
        {
            _backCount += step;
            _forwardCount -= step;
            _pendingHistory--;
        }
        HistoryChanged?.Invoke();
    }
    public void GoUp() => _browser?.BrowseToIDList(IntPtr.Zero, SBSP_PARENT);

    public void RefreshView()
    {
        WithShellView(v => v.Refresh());
        Dispatcher.UIThread.Post(UpdateItemCount, DispatcherPriority.Background);
    }

    /// <summary>Đường dẫn file/thư mục đang chọn; rỗng khi không chọn gì.</summary>
    public IReadOnlyList<string> SelectedPaths()
    {
        if (_browser == null) return [];
        var viewId = IID_IFolderView;
        if (_browser.GetCurrentView(ref viewId, out var viewPtr) != 0 || viewPtr == IntPtr.Zero) return [];
        try
        {
            var view = (IFolderView)Marshal.GetObjectForIUnknown(viewPtr);
            try
            {
                Marshal.ThrowExceptionForHR(view.ItemCount(SVGIO_SELECTION, out var count));
                if (count == 0) return [];
                if (_currentPidl == IntPtr.Zero) throw new InvalidOperationException("Chưa xác định được thư mục hiện tại.");

                var enumId = IID_IEnumIDList;
                Marshal.ThrowExceptionForHR(view.Items(SVGIO_SELECTION, ref enumId, out var enumPtr));
                try
                {
                    var items = (IEnumIDList)Marshal.GetObjectForIUnknown(enumPtr);
                    try
                    {
                        var paths = new List<string>(count);
                        for (int i = 0; i < count; i++)
                        {
                            Marshal.ThrowExceptionForHR(items.Next(1, out var child, out var fetched));
                            if (fetched != 1) throw new InvalidOperationException("Không đọc được mục đang chọn.");
                            try
                            {
                                var full = ILCombine(_currentPidl, child);
                                if (full == IntPtr.Zero) throw new OutOfMemoryException();
                                try
                                {
                                    paths.Add(GetName(full, SIGDN_FILESYSPATH)
                                        ?? throw new InvalidOperationException("Mục được chọn không có đường dẫn file."));
                                }
                                finally { ILFree(full); }
                            }
                            finally { ILFree(child); }
                        }
                        return paths;
                    }
                    finally { Marshal.ReleaseComObject(items); }
                }
                finally { Marshal.Release(enumPtr); }
            }
            finally { Marshal.ReleaseComObject(view); }
        }
        finally { Marshal.Release(viewPtr); }
    }

    /// <summary>Đưa focus bàn phím vào danh sách file của panel.</summary>
    public void FocusView() => WithShellView(v => v.UIActivate(SVUIA_ACTIVATE_FOCUS));

    /// <summary>Lập lịch chọn và focus tới mục có tên chỉ định khi view sẵn sàng.</summary>
    public void SelectItemOrSchedule(string itemName)
    {
        SelectItemName = itemName;
        TrySelectPendingItem(1);
    }

    private void TrySelectPendingItem(int attempt)
    {
        if (string.IsNullOrEmpty(SelectItemName)) return;
        if (SelectItem(SelectItemName))
        {
            SelectItemName = null;
            return;
        }
        if (attempt < 8)
        {
            DispatcherTimer.RunOnce(() => TrySelectPendingItem(attempt + 1), TimeSpan.FromMilliseconds(attempt * 80), DispatcherPriority.Background);
        }
        else
        {
            SelectItemName = null;
        }
    }

    /// <summary>Chọn và cuộn tới mục có tên chỉ định trong view hiện tại.</summary>
    public bool SelectItem(string itemName)
    {
        if (_browser is null || string.IsNullOrWhiteSpace(itemName)) return false;
        var viewId = IID_IFolderView;
        if (_browser.GetCurrentView(ref viewId, out var pointer) != 0 || pointer == IntPtr.Zero) return false;
        try
        {
            var view = (IFolderView)Marshal.GetObjectForIUnknown(pointer);
            try
            {
                if (view.ItemCount(SVGIO_ALL, out int count) != 0 || count <= 0) return false;
                string nameWithoutExt = Path.GetFileNameWithoutExtension(itemName);
                for (int i = 0; i < count; i++)
                {
                    if (view.Item(i, out var childPidl) == 0 && childPidl != IntPtr.Zero)
                    {
                        try
                        {
                            var name = GetName(childPidl, SIGDN_PARENTRELATIVEPARSING)
                                       ?? GetName(childPidl, SIGDN_NORMALDISPLAY);
                            if (name != null && (
                                string.Equals(name, itemName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(name, nameWithoutExt, StringComparison.OrdinalIgnoreCase)))
                            {
                                const uint flags = SVSI_SELECT | SVSI_DESELECTOTHERS | SVSI_ENSUREVISIBLE | SVSI_FOCUSED | SVSI_SELECTIONMARK;
                                view.SelectItem(i, flags);
                                FocusView();
                                return true;
                            }
                        }
                        finally
                        {
                            ILFree(childPidl);
                        }
                    }
                }
                return false;
            }
            finally
            {
                Marshal.ReleaseComObject(view);
            }
        }
        catch { return false; }
        finally { Marshal.Release(pointer); }
    }

    private void WithShellView(Action<IShellView> action)
    {
        if (_browser == null) return;
        var iid = IID_IShellView;
        if (_browser.GetCurrentView(ref iid, out var p) != 0 || p == IntPtr.Zero) return;
        try
        {
            var view = (IShellView)Marshal.GetObjectForIUnknown(p);
            action(view);
            Marshal.ReleaseComObject(view);
        }
        finally { Marshal.Release(p); }
    }

    private void ApplyTheme()
    {
        if (_hostHwnd != IntPtr.Zero) ShellTheme.ApplyToPane(_hostHwnd, _dark);
    }

    // ---- Bàn phím & chuột (gọi từ hook message của MainWindow) ----

    /// <summary>hwnd có thuộc cây cửa sổ của panel này không.</summary>
    public bool Owns(IntPtr hwnd) => _hostHwnd != IntPtr.Zero && (hwnd == _hostHwnd || IsChild(_hostHwnd, hwnd));

    /// <summary>Cho Explorer Browser xử lý phím tắt (Delete, F2, Ctrl+C/V, ...). Trả về true nếu đã xử lý.</summary>
    internal bool TranslateAccelerator(ref MSG msg)
    {
        return _browser is IInputObject io && io.TranslateAcceleratorIO(ref msg) == 0;
    }

    // ---- Nhận sự kiện từ COM ----

    private void OnViewCreated()
    {
        // View mới (list view mới) được tạo sau mỗi lần điều hướng: tô lại màu
        Dispatcher.UIThread.Post(ApplyTheme, DispatcherPriority.Background);
    }

    private void OnNavigationComplete(IntPtr pidl)
    {
        if (_pendingHistory > 0) _pendingHistory--;
        else if (_skipNextEntry) _skipNextEntry = false;
        else if (_currentPidl != IntPtr.Zero)
        {
            // Điều hướng mới (mở thư mục, ô địa chỉ, lên cấp...): thêm một bước lùi, xoá các bước tiến
            _backCount++;
            _forwardCount = 0;
        }
        HistoryChanged?.Invoke();
        _itemCount = 0;
        if (_currentPidl != IntPtr.Zero) ILFree(_currentPidl);
        _currentPidl = ILClone(pidl);
        string display = GetName(pidl, SIGDN_DESKTOPABSOLUTEEDITING)
                         ?? GetName(pidl, SIGDN_NORMALDISPLAY) ?? "";
        string parsing = GetName(pidl, SIGDN_DESKTOPABSOLUTEPARSING) ?? display;
        Navigated?.Invoke(display, parsing);
        Dispatcher.UIThread.Post(UpdateItemCount, DispatcherPriority.Background);
        Dispatcher.UIThread.Post(ApplyTheme, DispatcherPriority.Background);
        if (!string.IsNullOrEmpty(SelectItemName))
        {
            Dispatcher.UIThread.Post(() => TrySelectPendingItem(1), DispatcherPriority.Background);
        }
    }

    private void OnNavigationFailed()
    {
        if (_pendingHistory > 0) _pendingHistory--;
        _skipNextEntry = false;
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class BrowserSite(ExplorerHost owner) : IComServiceProvider, IExplorerPaneVisibility
    {
        public int QueryService(ref Guid service, ref Guid iid, out IntPtr result)
        {
            result = IntPtr.Zero;
            if (service != IID_IExplorerPaneVisibility)
                return unchecked((int)0x80004002); // E_NOINTERFACE
            var unknown = Marshal.GetIUnknownForObject(this);
            try { return Marshal.QueryInterface(unknown, in iid, out result); }
            finally { Marshal.Release(unknown); }
        }

        public int GetPaneState(ref Guid pane, out uint state)
        {
            // EPS_FORCE | EPS_DEFAULT_OFF (0x20002) hoặc EPS_FORCE | EPS_DEFAULT_ON (0x20001)
            if (pane == EP_NavPane) state = owner._showTree ? 0u : 0x20002u;
            else if (pane == EP_PreviewPane) state = owner._showPreview ? 0x20001u : 0x20002u;
            else state = 0;
            return 0;
        }
    }

    /// <summary>
    /// Lọc theo tên. DefView có thể gọi từ thread liệt kê nền, nên lớp này bất biến và chỉ dùng API thread-safe.
    /// </summary>
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class NameFilter(string text) : IFolderFilter
    {
        public string Text { get; } = text;

        public int ShouldShow(IntPtr psf, IntPtr pidlFolder, IntPtr pidlItem)
        {
            if (pidlFolder == IntPtr.Zero || pidlItem == IntPtr.Zero) return 0;
            var full = ILCombine(pidlFolder, pidlItem);
            if (full == IntPtr.Zero) return 0;
            try
            {
                var name = GetName(full, SIGDN_PARENTRELATIVEPARSING) ?? "";
                var display = GetName(full, SIGDN_NORMALDISPLAY) ?? "";
                bool match = name.Contains(Text, StringComparison.CurrentCultureIgnoreCase) ||
                             display.Contains(Text, StringComparison.CurrentCultureIgnoreCase);
                return match ? 0 : 1; // S_OK hiện, S_FALSE ẩn
            }
            finally { ILFree(full); }
        }

        // E_NOTIMPL: giữ cờ liệt kê mặc định (tôn trọng tuỳ chọn hiện file ẩn của Windows)
        public int GetEnumFlags(IntPtr psf, IntPtr pidlFolder, IntPtr phwnd, IntPtr pgrfFlags) => unchecked((int)0x80004001);
    }

    [ComVisible(true)]
    private sealed class EventSink(ExplorerHost owner) : IExplorerBrowserEvents
    {
        public int OnNavigationPending(IntPtr pidlFolder) => 0;
        public int OnViewCreated(object psv) { owner.OnViewCreated(); return 0; }
        public int OnNavigationComplete(IntPtr pidlFolder) { owner.OnNavigationComplete(pidlFolder); return 0; }
        public int OnNavigationFailed(IntPtr pidlFolder) { owner.OnNavigationFailed(); return 0; }
    }
}
