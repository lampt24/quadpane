using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
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
    private PaneView? _searchPane;
    private SearchResultsView? _searchView;
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(2) };
    private readonly DispatcherTimer _gitTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private CancellationTokenSource? _gitRefresh;
    private string? _gitFolder;
    private string? _tortoiseGit;
    private string? _gitStatusPath;
    private bool _gitBusy;
    private UpdateService.Release? _availableRelease;
    private bool _checkingUpdate;
    private bool _installingUpdate;
    private bool _startupSettingReady;
    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // Hook message của thread UI: phím/chuột trong cửa sổ Win32 của Explorer
    // không đi qua hệ thống input của Avalonia, nên phải bắt ở mức Win32.
    private readonly HookProcDelegate _hookProc;
    private IntPtr _hook;

    // Vị trí ô lưới của từng panel: 0 trên-trái, 1 trên-phải, 2 dưới-trái, 3 dưới-phải
    private static readonly (int Row, int Col)[] Cells = [(0, 0), (0, 2), (2, 0), (2, 2)];

    private enum Shortcut { None, Pane1, Pane2, Pane3, Pane4, Address, Search, Back, Forward, Up, Refresh, NewTab, CloseTab, NextTab, PreviousTab }

    // Constructor không tham số cho designer
    public MainWindow() : this(new LayoutData()) { }

    public MainWindow(LayoutData layout)
    {
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        Title = $"QuadPane v{version}";
        VersionLabel.Text = $"v{version}";
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey);
            StartupCheck.IsChecked = key?.GetValue("QuadPane") is string value &&
                value == $"\"{Environment.ProcessPath}\"";
        }
        _startupSettingReady = true;
        _layout = layout;
        _hookProc = GetMessageHook;

        for (int i = 0; i < 4; i++)
        {
            var paths = _layout.TabPaths[i] is { Length: > 0 } saved ? saved : [_layout.Paths[i]];
            var ids = _layout.TabIds[i] is { Length: > 0 } savedIds && savedIds.Length == paths.Length
                ? savedIds : Enumerable.Range(0, paths.Length).Select(j => j == 0 ? $"QuadPane.Pane{i}" : $"QuadPane.Pane{i}.Tab{Guid.NewGuid():N}").ToArray();
            var pane = new PaneView(i, paths, ids, _layout.ActiveTabs[i], _layout.ShowTree[i],
                LayoutStore.DefaultPath(i), _layout.TabHomes[i]);
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
        ActualThemeVariantChanged += (_, _) =>
        {
            ApplyThemeToNative();
            DispatcherTimer.RunOnce(ApplyThemeToNative, TimeSpan.FromMilliseconds(150), DispatcherPriority.Background);
        };
        _updateTimer.Tick += (_, _) => _ = CheckForUpdatesAsync();
        _gitTimer.Tick += (_, _) => { UpdateStatus(); RefreshGit(); };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); UpdateStatusNow(); };
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_hook == IntPtr.Zero)
            _hook = SetWindowsHookEx(WH_GETMESSAGE, _hookProc, IntPtr.Zero, GetCurrentThreadId());
        // HWND của cửa sổ chưa chắc sẵn sàng ngay trong Opened: dời sang lượt dispatcher sau
        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeToNative();
            HideDrawnTitle();
        }, DispatcherPriority.Background);
#if !DEBUG
        _updateTimer.Start();
        _ = CheckForUpdatesAsync();
#endif
        _gitTimer.Start();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_checkingUpdate || _installingUpdate || App.IsExiting) return;
        _checkingUpdate = true;
        try
        {
            var release = await UpdateService.GetNewReleaseAsync();
            if (App.IsExiting) return;
            _availableRelease = release;
            UpdateButton.IsVisible = release is not null;
            UpdateErrorButton.IsVisible = false;
            if (release is not null) UpdateButton.Content = $"Cập nhật {release.Tag}";
        }
        catch (Exception ex)
        {
            if (!UpdateErrorButton.IsVisible) App.LogCrash(ex);
            if (_availableRelease is null)
            {
                UpdateErrorButton.IsVisible = true;
                ToolTip.SetTip(UpdateErrorButton, $"{ex.Message}\nLog: {App.CrashLogPath}\nNhấn để thử lại");
            }
        }
        finally { _checkingUpdate = false; }
    }

    private void UpdateErrorButton_Click(object? sender, RoutedEventArgs e) => _ = CheckForUpdatesAsync();

    private async void UpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        var release = _availableRelease;
        if (release is null || _installingUpdate ||
            !UpdateService.Confirm(this, $"Tải và cài QuadPane {release.Tag}?")) return;

        _installingUpdate = true;
        UpdateButton.IsEnabled = false;
        try
        {
            StatusText.Text = $"Đang tải QuadPane {release.Tag}...";
            await UpdateService.InstallAsync(release);
            App.RequestExit();
            Close();
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            UpdateStatus();
            UpdateService.ShowError(this, $"Không thể cập nhật QuadPane: {ex.Message}");
            UpdateButton.IsEnabled = true;
            _installingUpdate = false;
        }
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

    public void OpenFolder(string path)
    {
        foreach (var pane in _panes)
        {
            int tab = pane.FindTab(path);
            if (tab < 0) continue;
            if (_mode == LayoutMode.Triple && pane.Index == 3)
            {
                _mode = LayoutMode.Quad;
                ApplyLayout();
            }
            pane.ActivateTab(tab);
            SetActive(pane);
            return;
        }
        _panes[0].OpenTab(path);
        SetActive(_panes[0]);
    }

    private void UpdateStatus()
    {
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void UpdateStatusNow()
    {
        if (!_installingUpdate && !_gitBusy) StatusText.Text = $"{_active.ItemCount:N0} mục";
        ToolTip.SetTip(StatusText, _active.DisplayPath);
        if (!string.Equals(_gitStatusPath, _active.CurrentPath, StringComparison.OrdinalIgnoreCase)) RefreshGit();
    }

    private async void RefreshGit()
    {
        _gitRefresh?.Cancel();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        _gitRefresh = cancellation;
        var folder = _active.CurrentPath;
        _gitStatusPath = folder;
        if (folder is null || !Directory.Exists(folder))
        {
            GitControls.IsVisible = false;
            GitActions.IsVisible = false;
            _gitFolder = null;
            _gitRefresh = null;
            return;
        }
        try
        {
            var info = await GitService.StatusAsync(folder, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            _gitFolder = folder;
            GitCountText.Text = $"Đã stage {info.Staged} · Chưa stage {info.Unstaged}";
            GitBranchText.Text = info.Branch.Length > 1000 ? info.Branch[..1000] : info.Branch;
            ToolTip.SetTip(GitBranchText, info.Branch);
            _tortoiseGit = GitService.FindTortoiseGit();
            GitLogButton.IsVisible = _tortoiseGit is not null;
            GitControls.IsVisible = true;
            GitActions.IsVisible = true;
        }
        catch (Exception)
        {
            if (cancellation.IsCancellationRequested) return;
            _gitFolder = null;
            GitControls.IsVisible = false;
            GitActions.IsVisible = false;
        }
        finally
        {
            if (_gitRefresh == cancellation) _gitRefresh = null;
        }
    }

    private void GitFetch_Click(object? sender, RoutedEventArgs e) => _ = RunGitActionAsync("fetch");
    private void GitPull_Click(object? sender, RoutedEventArgs e) => _ = RunGitActionAsync("pull");
    private void GitPush_Click(object? sender, RoutedEventArgs e) => _ = RunGitActionAsync("push");

    private void GitLog_Click(object? sender, RoutedEventArgs e)
    {
        if (_gitFolder is not { } folder || _tortoiseGit is not { } exe) return;
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = folder };
            start.ArgumentList.Add("/command:log");
            start.ArgumentList.Add($"/path:{folder}");
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Không mở được TortoiseGit log.");
        }
        catch (Exception ex)
        {
            UpdateService.ShowError(this, ex.Message, "TortoiseGit");
        }
    }

    private async Task RunGitActionAsync(string action)
    {
        if (_gitBusy || _gitFolder is not { } folder) return;
        _gitBusy = true;
        GitControls.IsEnabled = false;
        GitActions.IsEnabled = false;
        StatusText.Text = $"Git {action}...";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var args = action == "pull" ? new[] { "pull", "--ff-only" } : [action];
            await GitService.RunAsync(folder, args, timeout.Token);
            StatusText.Text = $"Git {action} xong";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Git {action} lỗi";
            UpdateService.ShowError(this, ex.Message, $"Git {action}");
        }
        finally
        {
            _gitBusy = false;
            GitControls.IsEnabled = true;
            GitActions.IsEnabled = true;
            RefreshGit();
        }
    }

    private void StartupCheck_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_startupSettingReady || !OperatingSystem.IsWindows()) return;
        var enabled = StartupCheck.IsChecked == true;
        try
        {
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (exe is null || !Path.GetFileName(exe).Equals("QuadPane.exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Chỉ bật được cho bản QuadPane đã đóng gói.");
                using var key = Registry.CurrentUser.CreateSubKey(StartupRegistryKey);
                key.SetValue("QuadPane", $"\"{exe}\"");
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: true);
                key?.DeleteValue("QuadPane", throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            _startupSettingReady = false;
            StartupCheck.IsChecked = !enabled;
            _startupSettingReady = true;
            UpdateService.ShowError(this, ex.Message, "Cài đặt QuadPane");
        }
    }

    private void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim() ?? "";
        if (_searchPane is { } owner && _searchView is { } view && owner.ContainsSearch(view))
        {
            if (_mode == LayoutMode.Triple && owner.Index == 3)
            {
                _mode = LayoutMode.Quad;
                ApplyLayout();
            }
            if (owner != _active || !owner.IsCurrentSearch(view)) owner.ActivateSearch(view);
            owner.Search(view, query);
            UpdateStatus();
            return;
        }
        if (query.Length == 0) return;
        _searchPane = _active;
        _searchView = _active.OpenSearchTab();
        _active.Search(_searchView, query);
        UpdateStatus();
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Key is Key.Enter or Key.Down) && _searchPane == _active &&
            _searchView is not null && _active.IsCurrentSearch(_searchView))
        {
            _searchView.FocusResults();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            e.Handled = true;
        }
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
        foreach (var button in new[] { QuadMode, TripleMode, DualMode, SingleMode })
            if (button.Content is Avalonia.Controls.Shapes.Path icon)
                icon.Classes.Set("selected", button.IsChecked == true);

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
        DispatcherTimer.RunOnce(ApplyThemeToNative, TimeSpan.FromMilliseconds(150), DispatcherPriority.Background);
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
        if (e.Source is Visual v &&
            (v.FindAncestorOfType<Button>(includeSelf: true) != null ||
             v.FindAncestorOfType<TextBox>(includeSelf: true) != null)) return;

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
        if (ctrl && !alt && vk == 0x09) return shift ? Shortcut.PreviousTab : Shortcut.NextTab;
        if (shift) return Shortcut.None;
        if (ctrl && !alt && vk == 0x54) return Shortcut.NewTab;
        if (ctrl && !alt && vk == 0x57) return Shortcut.CloseTab;
        if (ctrl && !alt && vk is >= 0x31 and <= 0x34) return Shortcut.Pane1 + (vk - 0x31);
        if ((ctrl && !alt && vk == 0x4C) || (alt && !ctrl && vk == 0x44)) return Shortcut.Address; // Ctrl+L, Alt+D
        if (ctrl && !alt && vk == 0x46) return Shortcut.Search; // Ctrl+F
        if (alt && !ctrl)
            return vk switch { 0x25 => Shortcut.Back, 0x27 => Shortcut.Forward, 0x26 => Shortcut.Up, _ => Shortcut.None };
        if (!ctrl && !alt && vk == 0x74) return Shortcut.Refresh; // F5
        return Shortcut.None;
    }

    private static Shortcut MapAvaloniaKey(Key key, bool ctrl, bool alt, bool shift)
    {
        if (ctrl && !alt && key == Key.Tab) return shift ? Shortcut.PreviousTab : Shortcut.NextTab;
        if (shift) return Shortcut.None;
        if (ctrl && !alt && key == Key.T) return Shortcut.NewTab;
        if (ctrl && !alt && key == Key.W) return Shortcut.CloseTab;
        if (ctrl && !alt && key is >= Key.D1 and <= Key.D4) return Shortcut.Pane1 + (key - Key.D1);
        if ((ctrl && !alt && key == Key.L) || (alt && !ctrl && key == Key.D)) return Shortcut.Address;
        if (ctrl && !alt && key == Key.F) return Shortcut.Search;
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
            case Shortcut.Search: FocusSearch(); break;
            case Shortcut.Back: _active.GoBack(); break;
            case Shortcut.Forward: _active.GoForward(); break;
            case Shortcut.Up: _active.GoUp(); break;
            case Shortcut.Refresh: _active.RefreshView(); break;
            case Shortcut.NewTab: _active.OpenTab(); break;
            case Shortcut.CloseTab: _active.CloseCurrentTab(); break;
            case Shortcut.NextTab: _active.SelectNextTab(1); break;
            case Shortcut.PreviousTab: _active.SelectNextTab(-1); break;
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
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _updateTimer.Stop();
        _gitTimer.Stop();
        _statusTimer.Stop();
        _gitRefresh?.Cancel();
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        if (UsesBothSplitters(_mode)) RememberRatios();

        _layout.Mode = _mode;
        _layout.ActivePane = _active.Index;
        for (int i = 0; i < 4; i++)
        {
            _layout.Paths[i] = _panes[i].CurrentPath ?? _layout.Paths[i];
            _layout.TabPaths[i] = _panes[i].TabPaths;
            _layout.TabHomes[i] = _panes[i].TabHomes;
            _layout.TabIds[i] = _panes[i].TabIds;
            _layout.ActiveTabs[i] = _panes[i].ActiveTabIndex;
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
