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
    private bool _dark;

    /// <summary>Thư mục mở đầu tiên (parsing name). Đặt trước khi control được nạp.</summary>
    public string? InitialPath { get; set; }

    /// <summary>Tên dùng để Windows lưu riêng trạng thái view (cột, kiểu xem) cho panel này.</summary>
    public string PropertyBagName { get; set; } = "QuadPane";

    /// <summary>Phát ra khi đã điều hướng xong: (tên hiển thị để sửa, parsing name để lưu).</summary>
    public event Action<string, string>? Navigated;

    public string? CurrentParsingName => GetName(_currentPidl, SIGDN_DESKTOPABSOLUTEPARSING);

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
    }

    private void DestroyBrowser()
    {
        if (_browser == null) return;
        if (_cookie != 0) _browser.Unadvise(_cookie);
        _cookie = 0;
        _sink = null;
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

    public bool Navigate(string path)
    {
        if (_browser == null || string.IsNullOrWhiteSpace(path)) return false;
        var pidl = ParsePath(Environment.ExpandEnvironmentVariables(path.Trim()));
        if (pidl == IntPtr.Zero) return false;
        try { return _browser.BrowseToIDList(pidl, SBSP_ABSOLUTE) >= 0; }
        finally { ILFree(pidl); }
    }

    public void GoBack() => _browser?.BrowseToIDList(IntPtr.Zero, SBSP_NAVIGATEBACK);
    public void GoForward() => _browser?.BrowseToIDList(IntPtr.Zero, SBSP_NAVIGATEFORWARD);
    public void GoUp() => _browser?.BrowseToIDList(IntPtr.Zero, SBSP_PARENT);

    public void RefreshView() => WithShellView(v => v.Refresh());

    /// <summary>Đưa focus bàn phím vào danh sách file của panel.</summary>
    public void FocusView() => WithShellView(v => v.UIActivate(SVUIA_ACTIVATE_FOCUS));

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
        if (_currentPidl != IntPtr.Zero) ILFree(_currentPidl);
        _currentPidl = ILClone(pidl);
        string display = GetName(pidl, SIGDN_DESKTOPABSOLUTEEDITING)
                         ?? GetName(pidl, SIGDN_NORMALDISPLAY) ?? "";
        string parsing = GetName(pidl, SIGDN_DESKTOPABSOLUTEPARSING) ?? display;
        Navigated?.Invoke(display, parsing);
        Dispatcher.UIThread.Post(ApplyTheme, DispatcherPriority.Background);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class BrowserSite(ExplorerHost owner) : IComServiceProvider, IExplorerPaneVisibility
    {
        public int QueryService(ref Guid service, ref Guid iid, out IntPtr result)
        {
            result = IntPtr.Zero;
            if (service != IID_IExplorerPaneVisibility || iid != IID_IExplorerPaneVisibility)
                return unchecked((int)0x80004002); // E_NOINTERFACE
            result = Marshal.GetComInterfaceForObject(this, typeof(IExplorerPaneVisibility));
            return 0;
        }

        public int GetPaneState(ref Guid pane, out uint state)
        {
            state = pane == EP_NavPane && !owner._showTree ? 0x20002u : 0u; // EPS_FORCE | EPS_DEFAULT_OFF
            return 0;
        }
    }

    [ComVisible(true)]
    private sealed class EventSink(ExplorerHost owner) : IExplorerBrowserEvents
    {
        public int OnNavigationPending(IntPtr pidlFolder) => 0;
        public int OnViewCreated(object psv) { owner.OnViewCreated(); return 0; }
        public int OnNavigationComplete(IntPtr pidlFolder) { owner.OnNavigationComplete(pidlFolder); return 0; }
        public int OnNavigationFailed(IntPtr pidlFolder) => 0;
    }
}
