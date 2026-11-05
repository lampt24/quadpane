using System.Runtime.InteropServices;
using static QuadPane.Shell.NativeMethods;

namespace QuadPane.Shell;

/// <summary>
/// Áp dụng light/dark cho phần Explorer nhúng (list view, header, cây thư mục, menu chuột phải)
/// và thanh tiêu đề cửa sổ.
/// Lưu ý: SetPreferredAppMode / AllowDarkModeForWindow / FlushMenuThemes là API KHÔNG công bố
/// của uxtheme.dll (gọi theo ordinal). Mọi lời gọi đều được bọc try/catch để không làm hỏng app
/// nếu một bản Windows nào đó thay đổi chúng.
/// </summary>
internal static class ShellTheme
{
    // Màu nền/chữ của danh sách file — trùng với PaneBackgroundBrush trong App.axaml
    public const uint DarkBack = 0x00202020;   // COLORREF = 0x00BBGGRR
    public const uint DarkText = 0x00F0F0F0;
    public const uint LightBack = 0x00FFFFFF;
    public const uint LightText = 0x00000000;

    private const uint LVM_SETBKCOLOR = 0x1001;
    private const uint LVM_SETTEXTCOLOR = 0x1024;
    private const uint LVM_SETTEXTBKCOLOR = 0x1026;
    private const uint TVM_SETBKCOLOR = 0x111D;
    private const uint TVM_SETTEXTCOLOR = 0x111E;

    private enum PreferredAppMode { Default = 0, AllowDark = 1, ForceDark = 2, ForceLight = 3 }

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(PreferredAppMode mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    [DllImport("uxtheme.dll", EntryPoint = "#133")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowDarkModeForWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool allow);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>Báo cho Windows chế độ màu mong muốn của cả process (ảnh hưởng menu chuột phải của shell).</summary>
    public static void SetProcessMode(ThemeMode mode)
    {
        Try(() =>
        {
            SetPreferredAppMode(mode switch
            {
                ThemeMode.Dark => PreferredAppMode.ForceDark,
                ThemeMode.Light => PreferredAppMode.ForceLight,
                _ => PreferredAppMode.AllowDark,
            });
            FlushMenuThemes();
        });
    }

    /// <summary>Thanh tiêu đề (và nút đóng/thu nhỏ của hệ thống) theo dark/light.</summary>
    public static void ApplyTitleBar(IntPtr topLevelHwnd, bool dark)
    {
        if (topLevelHwnd == IntPtr.Zero) return;
        int v = dark ? 1 : 0;
        Try(() => DwmSetWindowAttribute(topLevelHwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int)));
    }

    /// <summary>Tô lại mọi cửa sổ con của một panel Explorer.</summary>
    public static void ApplyToPane(IntPtr hostHwnd, bool dark)
    {
        if (hostHwnd == IntPtr.Zero) return;
        var back = (IntPtr)(dark ? DarkBack : LightBack);
        var text = (IntPtr)(dark ? DarkText : LightText);
        var systemFont = GetStockObject(17); // DEFAULT_GUI_FONT; Windows sở hữu handle này.

        EnumChildWindows(hostHwnd, (h, _) =>
        {
            Try(() => AllowDarkModeForWindow(h, dark));
            switch (GetClassName(h))
            {
                case "SysListView32":
                    SetWindowTheme(h, dark ? "DarkMode_ItemsView" : "ItemsView", null);
                    ApplySystemFont(h);
                    SendMessage(h, LVM_SETBKCOLOR, IntPtr.Zero, back);
                    SendMessage(h, LVM_SETTEXTBKCOLOR, IntPtr.Zero, back);
                    SendMessage(h, LVM_SETTEXTCOLOR, IntPtr.Zero, text);
                    break;
                case "SysHeader32":
                    SetWindowTheme(h, dark ? "DarkMode_ItemsView" : "ItemsView", null);
                    ApplySystemFont(h);
                    break;
                case "SysTreeView32":
                    SetWindowTheme(h, dark ? "DarkMode_Explorer" : "Explorer", null);
                    ApplySystemFont(h);
                    SendMessage(h, TVM_SETBKCOLOR, IntPtr.Zero, dark ? back : (IntPtr)(-1));
                    SendMessage(h, TVM_SETTEXTCOLOR, IntPtr.Zero, dark ? text : (IntPtr)(-1));
                    break;
                case "ScrollBar":
                    SetWindowTheme(h, dark ? "DarkMode_Explorer" : "Explorer", null);
                    break;
                default:
                    SendMessage(h, WM_THEMECHANGED, IntPtr.Zero, IntPtr.Zero);
                    break;
            }
            InvalidateRect(h, IntPtr.Zero, true);
            return true;
        }, IntPtr.Zero);

        void ApplySystemFont(IntPtr hwnd)
        {
            if (systemFont != IntPtr.Zero && SendMessage(hwnd, WM_GETFONT, IntPtr.Zero, IntPtr.Zero) != systemFont)
                SendMessage(hwnd, WM_SETFONT, systemFont, (IntPtr)1);
        }
    }

    private static void Try(Action a)
    {
        try { a(); } catch (Exception) { /* API không có trên bản Windows này */ }
    }
}

