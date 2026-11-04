using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuadPane.Shell;
using static QuadPane.Shell.NativeMethods;

namespace QuadPane;

public partial class MainWindow : Window
{
    private readonly PaneView[] _panes = new PaneView[4];
    private readonly LayoutData _layout;
    private PaneView _active;
    private LayoutMode _mode;

    // Hook message của thread UI: phím/chuột trong cửa sổ Win32 của Explorer
    // không đi qua hệ thống input của Avalonia, nên phải bắt ở mức Win32.
    private readonly HookProcDelegate _hookProc;
    private IntPtr _hook;

    // Vị trí ô lưới của từng panel: 0 trên-trái, 1 trên-phải, 2 dưới-trái, 3 dưới-phải
    private static readonly (int Row, int Col)[] Cells = [(0, 0), (0, 2), (2, 0), (2, 2)];

    private enum Shortcut { None, Pane1, Pane2, Pane3, Pane4, Address, Back, Forward, Up, Refresh }

    // Constructor không tham số cho designer
    public MainWindow() : this(new LayoutData()) { }

    public MainWindow(LayoutData layout)
    {
        InitializeComponent();
        _layout = layout;
        _hookProc = GetMessageHook;

        for (int i = 0; i < 4; i++)
        {
            var pane = new PaneView(i, _layout.Paths[i], _layout.ShowTree[i]);
            Grid.SetRow(pane, Cells[i].Row);
            Grid.SetColumn(pane, Cells[i].Col);
            pane.Activated += SetActive;
            pane.PathChanged += p => { if (p == _active) UpdateStatus(); };
            PaneGrid.Children.Insert(0, pane); // splitter nằm trên cùng
            _panes[i] = pane;
        }

        _active = _panes[Math.Clamp(_layout.ActivePane, 0, 3)];
        _mode = _layout.Mode;
        RestoreWindowPlacement();
        ApplyLayout();
        SetActive(_active);
        UpdateThemeButton();

        TitleBar.PointerPressed += TitleBar_PointerPressed;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        ActualThemeVariantChanged += (_, _) => ApplyThemeToNative();
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _hook = SetWindowsHookEx(WH_GETMESSAGE, _hookProc, IntPtr.Zero, GetCurrentThreadId());
        // HWND của cửa sổ chưa chắc sẵn sàng ngay trong Opened: dời sang lượt dispatcher sau
        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeToNative();
            HideDrawnTitle();
        }, DispatcherPriority.Background);
    }

    // ---- Panel đang hoạt động ----

    private void SetActive(PaneView pane)
    {
        bool changed = _active != pane;
        _active = pane;
        foreach (var p in _panes) p.IsActive = p == pane;
        UpdateStatus();
        // Bố cục 2 khung / 1 khung phụ thuộc panel đang hoạt động
        if (changed && _mode is LayoutMode.Dual or LayoutMode.Single) ApplyLayout();
    }

    private void UpdateStatus()
    {
        ActiveBadge.Text = $"PANEL {_active.Index + 1}";
        StatusText.Text = _active.DisplayPath;
        Title = $"{_active.DisplayPath} — QuadPane";
    }

    // ---- Bố cục ----

    private void Layout_Click(object? sender, RoutedEventArgs e)
    {
        if (UsesBothSplitters(_mode)) RememberRatios();
        _mode = sender == TripleMode ? LayoutMode.Triple
              : sender == DualMode ? LayoutMode.Dual
              : sender == SingleMode ? LayoutMode.Single
              : LayoutMode.Quad;
        ApplyLayout();
    }

    /// <summary>Chế độ có cả splitter dọc lẫn ngang (tỉ lệ chia đáng được ghi nhớ).</summary>
    private static bool UsesBothSplitters(LayoutMode m) => m is LayoutMode.Quad or LayoutMode.Triple;

    private void ApplyLayout()
    {
        QuadMode.IsChecked = _mode == LayoutMode.Quad;
        TripleMode.IsChecked = _mode == LayoutMode.Triple;
        DualMode.IsChecked = _mode == LayoutMode.Dual;
        SingleMode.IsChecked = _mode == LayoutMode.Single;

        // 3 khung: panel 1 chiếm cả cột trái, panel 2 và 3 xếp chồng ở cột phải
        // (panel 3 được dời sang ô dưới-phải để Ctrl+1..3 vẫn liền mạch).
        bool triple = _mode == LayoutMode.Triple;
        Grid.SetRowSpan(_panes[0], triple ? 3 : 1);
        Grid.SetColumn(_panes[2], triple ? 2 : 0);
        Grid.SetColumn(HSplitter, triple ? 2 : 0);
        Grid.SetColumnSpan(HSplitter, triple ? 1 : 3);

        bool[] visible = _mode switch
        {
            LayoutMode.Quad => [true, true, true, true],
            LayoutMode.Triple => [true, true, true, false],
            // 2 khung: hai panel cùng hàng với panel đang hoạt động
            LayoutMode.Dual => _active.Index < 2 ? [true, true, false, false] : [false, false, true, true],
            _ => [.. Enumerable.Range(0, 4).Select(i => i == _active.Index)],
        };
        for (int i = 0; i < 4; i++) _panes[i].IsVisible = visible[i];

        // Panel đang hoạt động bị ẩn (vd. panel 4 khi sang 3 khung) -> chuyển sang panel hiện đầu tiên
        if (!visible[_active.Index])
        {
            _active = _panes[Array.IndexOf(visible, true)];
            foreach (var p in _panes) p.IsActive = p == _active;
            UpdateStatus();
        }

        // Cột/hàng nào có panel hiển thị (panel 3 nằm ở cột phải khi 3 khung)
        bool leftUsed = visible[0] || (visible[2] && !triple);
        bool rightUsed = visible[1] || visible[3] || (visible[2] && triple);
        bool topUsed = visible[0] || visible[1], bottomUsed = visible[2] || visible[3];
        var cols = PaneGrid.ColumnDefinitions;
        var rows = PaneGrid.RowDefinitions;

        cols[0].Width = leftUsed ? Star(rightUsed ? _layout.LeftColumn : 1) : new GridLength(0);
        cols[2].Width = rightUsed ? Star(leftUsed ? _layout.RightColumn : 1) : new GridLength(0);
        rows[0].Height = topUsed ? Star(bottomUsed ? _layout.TopRow : 1) : new GridLength(0);
        rows[2].Height = bottomUsed ? Star(topUsed ? _layout.BottomRow : 1) : new GridLength(0);

        bool vSplit = leftUsed && rightUsed, hSplit = topUsed && bottomUsed;
        cols[1].Width = new GridLength(vSplit ? 8 : 0);
        rows[1].Height = new GridLength(hSplit ? 8 : 0);
        VSplitter.IsVisible = vSplit;
        HSplitter.IsVisible = hSplit;
    }

    private static GridLength Star(double v) => new(Math.Max(v, 0.05), GridUnitType.Star);

    /// <summary>Ghi nhớ tỉ lệ splitter hiện tại (chỉ có nghĩa ở chế độ 4 hoặc 3 khung).</summary>
    private void RememberRatios()
    {
        var cols = PaneGrid.ColumnDefinitions;
        var rows = PaneGrid.RowDefinitions;
        if (cols[0].ActualWidth > 0 && cols[2].ActualWidth > 0)
        {
            _layout.LeftColumn = cols[0].ActualWidth;
            _layout.RightColumn = cols[2].ActualWidth;
        }
        if (rows[0].ActualHeight > 0 && rows[2].ActualHeight > 0)
        {
            _layout.TopRow = rows[0].ActualHeight;
            _layout.BottomRow = rows[2].ActualHeight;
        }
    }

    private void Equalize_Click(object? sender, RoutedEventArgs e)
    {
        _layout.LeftColumn = _layout.RightColumn = _layout.TopRow = _layout.BottomRow = 1;
        ApplyLayout();
    }

    // ---- Theme ----

    private void Theme_Click(object? sender, RoutedEventArgs e)
    {
        // Vòng: Theo hệ thống → Sáng → Tối → Theo hệ thống
        _layout.Theme = _layout.Theme switch
        {
            ThemeMode.System => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
        ((App)Application.Current!).ApplyThemeMode(_layout.Theme);
        UpdateThemeButton();
        ApplyThemeToNative();
    }

    private void UpdateThemeButton()
    {
        (ThemeButton.Content, var tip) = _layout.Theme switch
        {
            ThemeMode.Light => ("", "Giao diện: Sáng (nhấn để chuyển sang Tối)"),
            ThemeMode.Dark => ("", "Giao diện: Tối (nhấn để theo hệ thống)"),
            _ => ("", "Giao diện: Theo hệ thống (nhấn để chuyển sang Sáng)"),
        };
        ToolTip.SetTip(ThemeButton, tip);
    }

    /// <summary>Đồng bộ phần native (thanh tiêu đề, Explorer) với theme hiện tại của Avalonia.</summary>
    private void ApplyThemeToNative()
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        ApplyNativeChrome();
        foreach (var p in _panes) p.Dark = dark;
    }

    /// <summary>Khung cửa sổ native (viền, menu hệ thống) theo dark/light.</summary>
    private void ApplyNativeChrome()
    {
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        ShellTheme.ApplyTitleBar(hwnd, ActualThemeVariant == ThemeVariant.Dark);
    }

    /// <summary>
    /// Avalonia 12 tự vẽ thanh tiêu đề (chữ tiêu đề + nút caption) trong lớp overlay của TopLevelHost,
    /// nằm ngoài cây visual của Window, nên style trong App.axaml không ghi đè được.
    /// App có header riêng: ẩn chữ tiêu đề và nút full screen, giữ nút thu nhỏ/phóng to/đóng.
    /// </summary>
    private void HideDrawnTitle()
    {
        if (this.GetVisualParent() is not Visual host) return;
        foreach (var v in host.GetVisualDescendants())
        {
            if (v is Control { Name: "PART_TitleTextPanel" or "PART_FullScreenButton" } c)
            {
                c.IsVisible = false;          // giá trị local: ưu tiên cao hơn template
                c.IsHitTestVisible = false;
            }
        }
    }

    // ---- Kéo cửa sổ bằng thanh tiêu đề tự vẽ ----

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // Bấm vào nút (Button/ToggleButton) thì không kéo cửa sổ
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null) return;

        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            BeginMoveDrag(e);
    }

    // ---- Bàn phím & chuột ----

    private IntPtr GetMessageHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam == PM_REMOVE)
        {
            try
            {
                var msg = Marshal.PtrToStructure<MSG>(lParam);
                if (FilterNativeMessage(ref msg))
                    Marshal.WriteInt32(lParam, IntPtr.Size, (int)WM_NULL); // "nuốt" message
            }
            catch (Exception ex) { App.LogCrash(ex); }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>Trả về true nếu message đã được xử lý và không nên dispatch tiếp.</summary>
    private bool FilterNativeMessage(ref MSG msg)
    {
        uint m = msg.message;
        bool isMouseDown = m is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN;
        bool isKeyDown = m is WM_KEYDOWN or WM_SYSKEYDOWN;
        if (!isMouseDown && !isKeyDown) return false;

        IntPtr target = msg.hwnd;
        var pane = _panes.FirstOrDefault(p => p.Owns(target));
        if (pane == null) return false;

        if (pane != _active)
        {
            var p = pane;
            Dispatcher.UIThread.Post(() => SetActive(p));
        }
        if (isMouseDown) return false;

        var shortcut = MapVirtualKey((int)msg.wParam, IsKeyDown(VK_CONTROL), IsKeyDown(VK_MENU), IsKeyDown(VK_SHIFT));
        if (shortcut != Shortcut.None)
        {
            Dispatcher.UIThread.Post(() => RunShortcut(shortcut));
            return true;
        }
        // Delete, F2, Ctrl+C/X/V/A, ... do chính Explorer xử lý
        return pane.TranslateAccelerator(ref msg);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        var shortcut = MapAvaloniaKey(e.Key, mods.HasFlag(KeyModifiers.Control), mods.HasFlag(KeyModifiers.Alt),
                                      mods.HasFlag(KeyModifiers.Shift));
        // Khi đang gõ ô địa chỉ, để Alt+←/→ và F5 hoạt động như phím của TextBox
        if (shortcut is Shortcut.Back or Shortcut.Forward && e.Source is TextBox) return;
        if (shortcut == Shortcut.None) return;
        RunShortcut(shortcut);
        e.Handled = true;
    }

    private static Shortcut MapVirtualKey(int vk, bool ctrl, bool alt, bool shift)
    {
        if (shift) return Shortcut.None;
        if (ctrl && !alt && vk is >= 0x31 and <= 0x34) return Shortcut.Pane1 + (vk - 0x31);
        if ((ctrl && !alt && vk == 0x4C) || (alt && !ctrl && vk == 0x44)) return Shortcut.Address; // Ctrl+L, Alt+D
        if (alt && !ctrl)
            return vk switch { 0x25 => Shortcut.Back, 0x27 => Shortcut.Forward, 0x26 => Shortcut.Up, _ => Shortcut.None };
        if (!ctrl && !alt && vk == 0x74) return Shortcut.Refresh; // F5
        return Shortcut.None;
    }

    private static Shortcut MapAvaloniaKey(Key key, bool ctrl, bool alt, bool shift)
    {
        if (shift) return Shortcut.None;
        if (ctrl && !alt && key is >= Key.D1 and <= Key.D4) return Shortcut.Pane1 + (key - Key.D1);
        if ((ctrl && !alt && key == Key.L) || (alt && !ctrl && key == Key.D)) return Shortcut.Address;
        if (alt && !ctrl)
            return key switch { Key.Left => Shortcut.Back, Key.Right => Shortcut.Forward, Key.Up => Shortcut.Up, _ => Shortcut.None };
        if (!ctrl && !alt && key == Key.F5) return Shortcut.Refresh;
        return Shortcut.None;
    }

    private void RunShortcut(Shortcut s)
    {
        switch (s)
        {
            case >= Shortcut.Pane1 and <= Shortcut.Pane4:
                var pane = _panes[s - Shortcut.Pane1];
                if (!pane.IsVisible)
                {
                    if (UsesBothSplitters(_mode)) RememberRatios();
                    _mode = LayoutMode.Quad;
                }
                SetActive(pane);
                ApplyLayout();
                pane.FocusView();
                break;
            case Shortcut.Address: _active.FocusAddress(); break;
            case Shortcut.Back: _active.GoBack(); break;
            case Shortcut.Forward: _active.GoForward(); break;
            case Shortcut.Up: _active.GoUp(); break;
            case Shortcut.Refresh: _active.RefreshView(); break;
        }
    }

    // ---- Lưu / khôi phục ----

    private void RestoreWindowPlacement()
    {
        Width = _layout.WindowWidth;
        Height = _layout.WindowHeight;
        if (_layout.WindowX is int x && _layout.WindowY is int y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (_layout.Maximized) WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        if (UsesBothSplitters(_mode)) RememberRatios();

        _layout.Mode = _mode;
        _layout.ActivePane = _active.Index;
        for (int i = 0; i < 4; i++)
        {
            _layout.Paths[i] = _panes[i].CurrentPath ?? _layout.Paths[i];
            _layout.ShowTree[i] = _panes[i].ShowTree;
        }

        _layout.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _layout.WindowX = Position.X;
            _layout.WindowY = Position.Y;
            _layout.WindowWidth = Width;
            _layout.WindowHeight = Height;
        }

        LayoutStore.Save(_layout);
    }
}
