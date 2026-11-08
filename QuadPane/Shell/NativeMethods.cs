using System.Runtime.InteropServices;

namespace QuadPane.Shell;

// ---- Các interface COM của Windows Shell (khai báo tay, đúng thứ tự vtable) ----

[ComImport, Guid("dfd3b6b5-c10c-4be9-85f6-a66969f402f6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerBrowser
{
    [PreserveSig] int Initialize(IntPtr hwndParent, ref RECT prc, ref FOLDERSETTINGS pfs);
    [PreserveSig] int Destroy();
    [PreserveSig] int SetRect(IntPtr phdwp, RECT rcBrowser);
    [PreserveSig] int SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string pszPropertyBag);
    [PreserveSig] int SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string pszEmptyText);
    [PreserveSig] int SetFolderSettings(ref FOLDERSETTINGS pfs);
    [PreserveSig] int Advise(IExplorerBrowserEvents psbe, out uint pdwCookie);
    [PreserveSig] int Unadvise(uint dwCookie);
    [PreserveSig] int SetOptions(ExplorerBrowserOptions dwFlag);
    [PreserveSig] int GetOptions(out ExplorerBrowserOptions pdwFlag);
    [PreserveSig] int BrowseToIDList(IntPtr pidl, uint uFlags);
    [PreserveSig] int BrowseToObject([MarshalAs(UnmanagedType.IUnknown)] object punk, uint uFlags);
    [PreserveSig] int FillFromObject([MarshalAs(UnmanagedType.IUnknown)] object punk, int dwFlags);
    [PreserveSig] int RemoveAll();
    [PreserveSig] int GetCurrentView(ref Guid riid, out IntPtr ppv);
}

[ComImport, Guid("361bbdc7-e6ee-4e13-be58-58e2240c810f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerBrowserEvents
{
    [PreserveSig] int OnNavigationPending(IntPtr pidlFolder);
    [PreserveSig] int OnViewCreated([MarshalAs(UnmanagedType.IUnknown)] object psv);
    [PreserveSig] int OnNavigationComplete(IntPtr pidlFolder);
    [PreserveSig] int OnNavigationFailed(IntPtr pidlFolder);
}

[ComImport, Guid("fc4801a3-2ba9-11cf-a229-00aa003d7352"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectWithSite
{
    [PreserveSig] int SetSite([MarshalAs(UnmanagedType.IUnknown)] object? site);
    [PreserveSig] int GetSite(ref Guid iid, out IntPtr site);
}

[ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IComServiceProvider
{
    [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out IntPtr result);
}

[ComImport, Guid("e07010ec-bc17-44c0-97b0-46c7c95b9edc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerPaneVisibility
{
    [PreserveSig] int GetPaneState(ref Guid pane, out uint state);
}

[ComImport, Guid("68284faa-6a48-11d0-8c78-00c04fd918b4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInputObject
{
    [PreserveSig] int UIActivateIO([MarshalAs(UnmanagedType.Bool)] bool fActivate, ref MSG pMsg);
    [PreserveSig] int HasFocusIO();
    [PreserveSig] int TranslateAcceleratorIO(ref MSG pMsg);
}

[ComImport, Guid("000214e3-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellView
{
    // IOleWindow
    [PreserveSig] int GetWindow(out IntPtr phwnd);
    [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
    // IShellView (chỉ khai báo tới các hàm cần dùng)
    [PreserveSig] int TranslateAccelerator(ref MSG pmsg);
    [PreserveSig] int EnableModeless([MarshalAs(UnmanagedType.Bool)] bool fEnable);
    [PreserveSig] int UIActivate(uint uState);
    [PreserveSig] int Refresh();
}

[ComImport, Guid("cde725b0-ccc9-4519-917e-325d72fab4ce"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFolderView
{
    [PreserveSig] int GetCurrentViewMode(out uint mode);
    [PreserveSig] int SetCurrentViewMode(uint mode);
    [PreserveSig] int GetFolder(ref Guid iid, out IntPtr result);
    [PreserveSig] int Item(int index, out IntPtr pidl);
    [PreserveSig] int ItemCount(uint flags, out int count);
    [PreserveSig] int Items(uint flags, ref Guid iid, out IntPtr result);
}

[ComImport, Guid("000214f2-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumIDList
{
    [PreserveSig] int Next(uint count, out IntPtr pidl, out uint fetched);
}

[Flags]
internal enum ExplorerBrowserOptions : uint
{
    None = 0,
    NavigateOnce = 0x1,
    ShowFrames = 0x2,          // hiện command bar và các pane; IExplorerPaneVisibility chọn pane cần ẩn
    AlwaysNavigate = 0x4,
    NoTravelLog = 0x8,
    NoWrapperWindow = 0x10,
    HtmlSharepointView = 0x20,
    NoBorder = 0x40,
    NoPersistViewState = 0x80,
}

/// <summary>Bộ lọc mục hiển thị của Explorer Browser (gõ để lọc).</summary>
[ComImport, Guid("9cc22886-dc8e-11d2-b1d0-00c04f8eeb3e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFolderFilter
{
    [PreserveSig] int ShouldShow(IntPtr psf, IntPtr pidlFolder, IntPtr pidlItem);
    [PreserveSig] int GetEnumFlags(IntPtr psf, IntPtr pidlFolder, IntPtr phwnd, IntPtr pgrfFlags);
}

[ComImport, Guid("c0a651f5-b48b-11d2-b5ed-006097c686f6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFolderFilterSite
{
    [PreserveSig] int SetFilter([MarshalAs(UnmanagedType.IUnknown)] object? punk);
}

// SHFILEOPSTRUCTW: trên x86 struct được pack 1 byte, trên x64/ARM64 theo căn lề tự nhiên.
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SHFILEOPSTRUCT64
{
    public IntPtr hwnd;
    public uint wFunc;
    [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
    [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
    public ushort fFlags;
    public int fAnyOperationsAborted;
    public IntPtr hNameMappings;
    [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
internal struct SHFILEOPSTRUCT32
{
    public IntPtr hwnd;
    public uint wFunc;
    [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
    [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
    public ushort fFlags;
    public int fAnyOperationsAborted;
    public IntPtr hNameMappings;
    [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;
    public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct FOLDERSETTINGS
{
    public uint ViewMode;
    public uint fFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public int ptX;
    public int ptY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CWPSTRUCT
{
    public IntPtr lParam;
    public IntPtr wParam;
    public uint message;
    public IntPtr hwnd;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WNDCLASSEX
{
    public int cbSize;
    public uint style;
    public IntPtr lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public IntPtr hInstance;
    public IntPtr hIcon;
    public IntPtr hCursor;
    public IntPtr hbrBackground;
    public string? lpszMenuName;
    public string lpszClassName;
    public IntPtr hIconSm;
}

internal delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
internal delegate IntPtr HookProcDelegate(int code, IntPtr wParam, IntPtr lParam);
internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

internal static class NativeMethods
{
    public static readonly Guid CLSID_ExplorerBrowser = new("71f96385-ddd6-48d3-a0c1-ae06e8b055fb");
    public static readonly Guid IID_IShellView = new("000214e3-0000-0000-c000-000000000046");
    public static readonly Guid IID_IFolderView = new("cde725b0-ccc9-4519-917e-325d72fab4ce");
    public static readonly Guid IID_IEnumIDList = new("000214f2-0000-0000-c000-000000000046");
    public static readonly Guid IID_IExplorerPaneVisibility = new("e07010ec-bc17-44c0-97b0-46c7c95b9edc");
    public static readonly Guid EP_NavPane = new("cb316b22-25f7-42b8-8a09-540d23a43c2f");
    public static readonly Guid EP_PreviewPane = new("893c63d1-45c8-4d17-be19-223be71be365");
    public const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;
    public const uint FO_MOVE = 1, FO_COPY = 2;
    public const ushort FOF_ALLOWUNDO = 0x40;

    public const uint FVM_DETAILS = 4;

    // Cờ cho BrowseToIDList
    public const uint SBSP_ABSOLUTE = 0x0000;
    public const uint SBSP_PARENT = 0x2000;
    public const uint SBSP_NAVIGATEBACK = 0x4000;
    public const uint SBSP_NAVIGATEFORWARD = 0x8000;

    public const uint SVUIA_ACTIVATE_FOCUS = 2;
    public const uint SVGIO_SELECTION = 1;

    // SIGDN
    public const uint SIGDN_NORMALDISPLAY = 0x00000000;
    public const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;
    public const uint SIGDN_DESKTOPABSOLUTEEDITING = 0x8004c000;
    public const uint SIGDN_FILESYSPATH = 0x80058000;

    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_CLIPCHILDREN = 0x02000000;
    public const int WS_CLIPSIBLINGS = 0x04000000;

    public const uint WM_NULL = 0x0000;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_THEMECHANGED = 0x031A;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_XBUTTONDOWN = 0x020B;
    public const uint WM_XBUTTONUP = 0x020C;
    public const uint WM_XBUTTONDBLCLK = 0x020D;
    public const uint WM_APPCOMMAND = 0x0319;

    public const int WH_GETMESSAGE = 3;
    public const int WH_CALLWNDPROC = 4;
    public const int PM_REMOVE = 1;

    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    // Nhiều driver chuột (Logitech, Razer, ...) đổi nút Back/Forward thành các phím này
    public const int VK_BROWSER_BACK = 0xA6, VK_BROWSER_FORWARD = 0xA7;

    // ---- user32 ----

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsChild(IntPtr parent, IntPtr child);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProcDelegate fn, IntPtr hMod, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int vk);

    [DllImport("user32.dll")]
    public static extern int GetMessageTime();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc fn, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hwnd, char[] buf, int max);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr cursor);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? name);

    public static bool IsKeyDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;

    public static string GetClassName(IntPtr hwnd)
    {
        var buf = new char[256];
        int n = GetClassName(hwnd, buf, buf.Length);
        return new string(buf, 0, Math.Max(0, n));
    }

    // ---- shell32 ----

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    public static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdn, out IntPtr ppszName);

    [DllImport("shell32.dll")]
    public static extern IntPtr ILClone(IntPtr pidl);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation64(ref SHFILEOPSTRUCT64 op);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation32(ref SHFILEOPSTRUCT32 op);

    /// <summary>
    /// Sao chép / di chuyển bằng hộp thoại tiến trình của Windows (hỏi khi trùng tên, hỗ trợ Ctrl+Z trong Explorer).
    /// Trả về false nếu người dùng huỷ.
    /// </summary>
    public static bool ShellTransfer(IntPtr owner, IReadOnlyList<string> sources, string target, bool move)
    {
        string from = string.Join('\0', sources) + "\0\0";
        string to = target + "\0\0";
        uint func = move ? FO_MOVE : FO_COPY;
        int result, aborted;
        if (IntPtr.Size == 4)
        {
            var op = new SHFILEOPSTRUCT32 { hwnd = owner, wFunc = func, pFrom = from, pTo = to, fFlags = FOF_ALLOWUNDO };
            result = SHFileOperation32(ref op);
            aborted = op.fAnyOperationsAborted;
        }
        else
        {
            var op = new SHFILEOPSTRUCT64 { hwnd = owner, wFunc = func, pFrom = from, pTo = to, fFlags = FOF_ALLOWUNDO };
            result = SHFileOperation64(ref op);
            aborted = op.fAnyOperationsAborted;
        }
        if (result != 0 && aborted == 0)
            throw new InvalidOperationException($"Windows báo lỗi khi {(move ? "di chuyển" : "sao chép")} (mã 0x{result:X}).");
        return aborted == 0;
    }

    [DllImport("shell32.dll")]
    public static extern IntPtr ILCombine(IntPtr parent, IntPtr child);

    [DllImport("shell32.dll")]
    public static extern void ILFree(IntPtr pidl);

    /// <summary>Lấy tên của một PIDL theo kiểu SIGDN; trả về null nếu thất bại.</summary>
    public static string? GetName(IntPtr pidl, uint sigdn)
    {
        if (pidl == IntPtr.Zero || SHGetNameFromIDList(pidl, sigdn, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    /// <summary>Chuyển đường dẫn (C:\..., ::{GUID}, \\server\share...) thành PIDL. Người gọi phải ILFree.</summary>
    public static IntPtr ParsePath(string path)
    {
        return SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) == 0 ? pidl : IntPtr.Zero;
    }
}
