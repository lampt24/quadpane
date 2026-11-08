using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
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
    private SettingsWindow? _settingsWindow;

    // Hook message của thread UI: phím/chuột trong cửa sổ Win32 của Explorer
    // không đi qua hệ thống input của Avalonia, nên phải bắt ở mức Win32.
    private readonly HookProcDelegate _hookProc;
    private readonly HookProcDelegate _sentMessageHookProc;
    private IntPtr _hook;
    private IntPtr _sentMessageHook;

    // Vị trí ô lưới của từng panel: 0 trên-trái, 1 trên-phải, 2 dưới-trái, 3 dưới-phải
    private static readonly (int Row, int Col)[] Cells = [(0, 0), (0, 2), (2, 0), (2, 2)];

    private Dictionary<KeyChord, CommandId> _keyMap = new();
    private PaneView? _previous;
    private readonly List<(int Pane, string Path, string? Home)> _closedTabs = [];
    private const int MaxClosedTabs = 25;
    private CommandPalette? _palette;

    // Constructor không tham số cho designer
    public MainWindow() : this(new LayoutData()) { }

    public MainWindow(LayoutData layout)
    {
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        Title = $"QuadPane v{version}";
        VersionLabel.Text = $"v{version}";
        _layout = layout;
        _hookProc = GetMessageHook;
        _sentMessageHookProc = SentMessageHook;

        for (int i = 0; i < 4; i++)
        {
            var paths = _layout.TabPaths[i] is { Length: > 0 } saved ? saved : [_layout.Paths[i]];
            var ids = _layout.TabIds[i] is { Length: > 0 } savedIds && savedIds.Length == paths.Length
                ? savedIds : Enumerable.Range(0, paths.Length).Select(j => j == 0 ? $"QuadPane.Pane{i}" : $"QuadPane.Pane{i}.Tab{Guid.NewGuid():N}").ToArray();
            var pane = new PaneView(i, paths, ids, _layout.ActiveTabs[i], _layout.ShowTree[i],
                LayoutStore.DefaultPath(i), _layout.TabHomes[i], _layout.ShowPreview[i]);
            Grid.SetRow(pane, Cells[i].Row);
            Grid.SetColumn(pane, Cells[i].Col);
            pane.Activated += SetActive;
            pane.PathChanged += p => { if (p == _active) UpdateStatus(); };
            pane.ApplySettings(_layout);
            pane.TabClosed += OnTabClosed;
            pane.FolderVisited += (_, path) => _layout.AddRecent(path);
            pane.FavoritesRequested += ShowFavoritesMenu;
            pane.TabDropped += OnTabDropped;
            pane.TabMoveRequested += (source, index, target) => MoveTab(source, index, _panes[target]);
            PaneGrid.Children.Insert(0, pane); // splitter nằm trên cùng
            _panes[i] = pane;
        }

        _keyMap = CommandCatalog.BuildMap(_layout);
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
        if (_sentMessageHook == IntPtr.Zero)
            _sentMessageHook = SetWindowsHookEx(WH_CALLWNDPROC, _sentMessageHookProc, IntPtr.Zero, GetCurrentThreadId());
        // HWND của cửa sổ chưa chắc sẵn sàng ngay trong Opened: dời sang lượt dispatcher sau
        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeToNative();
            HideDrawnTitle();
        }, DispatcherPriority.Background);
#if !DEBUG
        if (_layout.AutoCheckUpdates)
        {
            _updateTimer.Start();
            _ = CheckForUpdatesAsync();
        }
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
        if (changed) _previous = _active;
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
        if (!_layout.ShowGitBar || folder is null || !Directory.Exists(folder))
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
            GitSwitchButton.IsVisible = _tortoiseGit is not null;
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

    private void GitLog_Click(object? sender, RoutedEventArgs e) => OpenTortoiseGit("log");
    private void GitSwitch_Click(object? sender, RoutedEventArgs e) => OpenTortoiseGit("switch");

    private void OpenTortoiseGit(string command)
    {
        if (_gitFolder is not { } folder || _tortoiseGit is not { } exe) return;
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = folder };
            start.ArgumentList.Add($"/command:{command}");
            start.ArgumentList.Add($"/path:{folder}");
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Không mở được TortoiseGit.");
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

    // ---- Cài đặt ----

    internal LayoutData Settings => _layout;
    internal string? AvailableUpdateTag => _availableRelease?.Tag;

    private void Settings_Click(object? sender, RoutedEventArgs e) => OpenSettings();

    internal void OpenSettings(string? page = null)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show(this);
        }
        else _settingsWindow.Activate();
        if (page is not null) _settingsWindow.ShowPage(page);
    }

    /// <summary>Gọi sau khi cửa sổ Cài đặt đổi một tuỳ chọn: áp dụng ngay và lưu.</summary>
    internal void ApplySettings()
    {
        _keyMap = CommandCatalog.BuildMap(_layout);
        foreach (var pane in _panes) pane.ApplySettings(_layout);
#if !DEBUG
        if (_layout.AutoCheckUpdates) _updateTimer.Start();
        else _updateTimer.Stop();
#endif
        RefreshGit();
        LayoutStore.Save(_layout);
    }

    /// <summary>Kiểm tra cập nhật theo yêu cầu; trả về nội dung để hiển thị.</summary>
    internal async Task<string> CheckForUpdatesNowAsync()
    {
        try
        {
            var release = await UpdateService.GetNewReleaseAsync();
            _availableRelease = release;
            UpdateButton.IsVisible = release is not null;
            UpdateErrorButton.IsVisible = false;
            if (release is null) return "Bạn đang dùng phiên bản mới nhất.";
            UpdateButton.Content = $"Cập nhật {release.Tag}";
            return $"Có phiên bản mới {release.Tag}. Bấm nút cập nhật trên thanh trạng thái để cài.";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            return $"Không kiểm tra được: {ex.Message}";
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

    private void Layout_Click(object? sender, RoutedEventArgs e) => SetMode(
        sender == TripleMode ? LayoutMode.Triple
        : sender == DualMode ? LayoutMode.Dual
        : sender == SingleMode ? LayoutMode.Single
        : LayoutMode.Quad);

    private void SetMode(LayoutMode mode)
    {
        if (UsesBothSplitters(_mode)) RememberRatios();
        _mode = mode;
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
        cols[1].Width = new GridLength(vSplit ? 4 : 0);
        rows[1].Height = new GridLength(hSplit ? 4 : 0);
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

    // Vòng: Theo hệ thống → Sáng → Tối → Theo hệ thống
    private void Theme_Click(object? sender, RoutedEventArgs e) => SetTheme(_layout.Theme switch
    {
        ThemeMode.System => ThemeMode.Light,
        ThemeMode.Light => ThemeMode.Dark,
        _ => ThemeMode.System,
    });

    internal void SetTheme(ThemeMode mode)
    {
        _layout.Theme = mode;
        ((App)Application.Current!).ApplyThemeMode(_layout.Theme);
        _settingsWindow?.SyncTheme();
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

    // Một số chuột/driver/bàn phím gửi Back/Forward bằng WM_APPCOMMAND (SendMessage) thay vì WM_XBUTTON.
    // WH_GETMESSAGE không thấy message gửi đồng bộ, nên bắt bằng WH_CALLWNDPROC. Message này có thể tới
    // bất kỳ cửa sổ con nào (Explorer nhúng, cửa sổ chứa NativeControlHost) rồi được DefWindowProc
    // chuyển ngược lên cha, nên cùng một lần bấm sẽ đi qua hook nhiều lần: chỉ xử lý lần đầu.
    private IntPtr SentMessageHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                var message = Marshal.PtrToStructure<CWPSTRUCT>(lParam);
                if (message.message == WM_APPCOMMAND && IsOwnWindow(message.hwnd))
                {
                    var navigation = MapAppCommand(message.lParam);
                    int time = GetMessageTime();
                    if (navigation is { } nav && !(_appCommandHandled && _lastAppCommandTime == time))
                    {
                        _appCommandHandled = true;
                        _lastAppCommandTime = time;
                        var pane = _panes.FirstOrDefault(p => p.Owns(message.hwnd)) ?? PaneUnderMouse() ?? _active;
                        Dispatcher.UIThread.Post(() => NavigateWithMouse(pane, nav));
                    }
                }
            }
            catch (Exception ex) { App.LogCrash(ex); }
        }
        return CallNextHookEx(_sentMessageHook, code, wParam, lParam);
    }

    private bool _appCommandHandled;
    private int _lastAppCommandTime;

    /// <summary>Cửa sổ chính hoặc một cửa sổ con của nó (Explorer nhúng, ...).</summary>
    private bool IsOwnWindow(IntPtr hwnd)
    {
        var main = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        return main != IntPtr.Zero && hwnd != IntPtr.Zero && (hwnd == main || IsChild(main, hwnd));
    }

    private PaneView? PaneUnderMouse()
    {
        if (!GetCursorPos(out var cursor)) return null;
        var hwnd = WindowFromPoint(cursor);
        var nativePane = _panes.FirstOrDefault(p => p.Owns(hwnd));
        if (nativePane is not null) return nativePane;
        var visual = this.InputHitTest(this.PointToClient(new PixelPoint(cursor.X, cursor.Y))) as Visual;
        return visual?.FindAncestorOfType<PaneView>(includeSelf: true);
    }

    private void NavigateWithMouse(PaneView pane, CommandId navigation)
    {
        SetActive(pane);
        if (navigation == CommandId.Back) pane.GoBack();
        else pane.GoForward();
    }

    /// <summary>Trả về true nếu message đã được xử lý và không nên dispatch tiếp.</summary>
    private bool FilterNativeMessage(ref MSG msg)
    {
        uint m = msg.message;
        bool isMouseDown = m is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN;
        bool isXButton = m is WM_XBUTTONDOWN or WM_XBUTTONUP or WM_XBUTTONDBLCLK;
        bool isKeyDown = m is WM_KEYDOWN or WM_SYSKEYDOWN;
        if (!isMouseDown && !isXButton && !isKeyDown) return false;

        IntPtr target = msg.hwnd;
        var pane = _panes.FirstOrDefault(p => p.Owns(target));

        // Nút Back/Forward của chuột: xử lý cho mọi vùng trong cửa sổ (Explorer nhúng lẫn phần Avalonia),
        // điều hướng panel đang trỏ tới. Nuốt cả DOWN/UP/DBLCLK để DefWindowProc không sinh thêm
        // WM_APPCOMMAND (tránh lùi 2 lần). Bấm nhanh liên tiếp, Windows gửi DBLCLK thay cho DOWN thứ hai:
        // phải tính cả DBLCLK thì bấm 2 lần mới lùi đủ 2 bước như File Explorer.
        if (isXButton)
        {
            if (pane == null && !IsOwnWindow(target)) return false;
            if (MapXButton(msg.wParam) is not { } navigation) return false;
            if (m is WM_XBUTTONDOWN or WM_XBUTTONDBLCLK)
            {
                var p = pane ?? PaneUnderMouse() ?? _active;
                Dispatcher.UIThread.Post(() => NavigateWithMouse(p, navigation));
            }
            return true;
        }

        // Phím Browser Back/Forward (driver chuột hay đổi nút bên thành phím này). Explorer nhúng
        // không tự điều hướng với phím này nên phải tự xử lý; nuốt để không sinh thêm WM_APPCOMMAND.
        if (isKeyDown && (int)msg.wParam is VK_BROWSER_BACK or VK_BROWSER_FORWARD && (pane != null || IsOwnWindow(target)))
        {
            var navigation = (int)msg.wParam == VK_BROWSER_BACK ? CommandId.Back : CommandId.Forward;
            var p = pane ?? _active;
            Dispatcher.UIThread.Post(() => NavigateWithMouse(p, navigation));
            return true;
        }

        if (pane == null) return false;

        if (pane != _active)
        {
            var p = pane;
            Dispatcher.UIThread.Post(() => SetActive(p));
        }
        if (isMouseDown) return false;

        var modifiers = (IsKeyDown(VK_CONTROL) ? KeyModifiers.Control : 0) |
                        (IsKeyDown(VK_MENU) ? KeyModifiers.Alt : 0) |
                        (IsKeyDown(VK_SHIFT) ? KeyModifiers.Shift : 0);
        var key = KeyChord.FromVirtualKey((int)msg.wParam);
        if (key != Key.None && _keyMap.TryGetValue(KeyChord.Create(key, modifiers), out var command))
        {
            Dispatcher.UIThread.Post(() => RunCommand(command));
            return true;
        }
        // Delete, F2, Ctrl+C/X/V/A, ... do chính Explorer xử lý
        return pane.TranslateAccelerator(ref msg);
    }

    private static CommandId? MapXButton(IntPtr wParam) => ((wParam.ToInt64() >> 16) & 0xFFFF) switch
    {
        1 => CommandId.Back,
        2 => CommandId.Forward,
        _ => null,
    };

    private static CommandId? MapAppCommand(IntPtr lParam) => ((lParam.ToInt64() >> 16) & 0x0FFF) switch
    {
        1 => CommandId.Back,
        2 => CommandId.Forward,
        _ => null,
    };

    internal static void SelfCheckMouseNavigation()
    {
        if (MapXButton((IntPtr)0x10000) != CommandId.Back ||
            MapXButton((IntPtr)0x20000) != CommandId.Forward ||
            MapXButton(IntPtr.Zero) is not null ||
            MapAppCommand((IntPtr)0x10000) != CommandId.Back ||
            MapAppCommand((IntPtr)0x20000) != CommandId.Forward ||
            MapAppCommand(unchecked((IntPtr)0x80010000u)) != CommandId.Back ||
            MapAppCommand(IntPtr.Zero) is not null)
            throw new Exception("Mouse navigation mapping failed.");
        CommandCatalog.SelfCheck();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (KeyChord.IsModifierKey(e.Key)) return;
        var chord = KeyChord.Create(e.Key, e.KeyModifiers);
        if (!_keyMap.TryGetValue(chord, out var command)) return;
        // Đang gõ trong ô nhập: để các phím soạn thảo (chữ, Alt+←/→, Ctrl+C/V...) cho TextBox
        if (e.Source is TextBox && IsTextEditingChord(chord)) return;
        RunCommand(command);
        e.Handled = true;
    }

    private static bool IsTextEditingChord(KeyChord chord)
    {
        bool ctrl = chord.Modifiers.HasFlag(KeyModifiers.Control), alt = chord.Modifiers.HasFlag(KeyModifiers.Alt);
        if (!ctrl && !alt) return chord.Key is not (>= Key.F1 and <= Key.F24);
        if (alt && !ctrl) return chord.Key is Key.Left or Key.Right or Key.Up or Key.Down;
        return ctrl && !alt && chord.Key is Key.A or Key.C or Key.V or Key.X or Key.Z or Key.Y
            or Key.Left or Key.Right or Key.Back or Key.Delete or Key.Home or Key.End;
    }

    internal void RunCommand(CommandId command)
    {
        switch (command)
        {
            case >= CommandId.Pane1 and <= CommandId.Pane4:
                var pane = _panes[command - CommandId.Pane1];
                if (!pane.IsVisible)
                {
                    if (UsesBothSplitters(_mode)) RememberRatios();
                    _mode = LayoutMode.Quad;
                }
                SetActive(pane);
                ApplyLayout();
                pane.FocusView();
                break;
            case CommandId.Address: _active.FocusAddress(); break;
            case CommandId.Search: FocusSearch(); break;
            case CommandId.Filter: _active.FocusFilter(); break;
            case CommandId.Back: _active.GoBack(); break;
            case CommandId.Forward: _active.GoForward(); break;
            case CommandId.Up: _active.GoUp(); break;
            case CommandId.Refresh: _active.RefreshView(); break;
            case CommandId.NewTab: _active.OpenTab(); break;
            case CommandId.CloseTab: _active.CloseCurrentTab(); break;
            case CommandId.ReopenTab: ReopenClosedTab(); break;
            case CommandId.DuplicateTab: _active.DuplicateCurrentTab(); break;
            case CommandId.NextTab: _active.SelectNextTab(1); break;
            case CommandId.PreviousTab: _active.SelectNextTab(-1); break;
            case CommandId.Favorites: _active.ShowFavorites(); break;
            case CommandId.AddFavorite: ToggleFavorite(_active.CurrentPath); break;
            case CommandId.CopyToOther: TransferToOtherPane(move: false); break;
            case CommandId.MoveToOther: TransferToOtherPane(move: true); break;
            case CommandId.OpenTerminal: _active.OpenTerminal(); break;
            case CommandId.ToggleTree: _active.ToggleTree(); break;
            case CommandId.TogglePreview: _active.TogglePreview(); break;
            case CommandId.SaveWorkspace: _ = SaveWorkspaceAsync(); break;
            case CommandId.LayoutQuad: SetMode(LayoutMode.Quad); break;
            case CommandId.LayoutTriple: SetMode(LayoutMode.Triple); break;
            case CommandId.LayoutDual: SetMode(LayoutMode.Dual); break;
            case CommandId.LayoutSingle: SetMode(LayoutMode.Single); break;
            case CommandId.Equalize: Equalize_Click(null, new RoutedEventArgs()); break;
            case CommandId.ToggleTheme: Theme_Click(null, new RoutedEventArgs()); break;
            case CommandId.CommandPalette: OpenCommandPalette(); break;
            case CommandId.Settings: OpenSettings(); break;
        }
    }

    private KeyGesture? GestureFor(CommandId command) =>
        CommandCatalog.Gestures(_layout, command) is [var first, ..] ? new KeyGesture(first.Key, first.Modifiers) : null;

    // ---- Tab đã đóng ----

    private void OnTabClosed(PaneView pane, string path, string? home)
    {
        _closedTabs.Insert(0, (pane.Index, path, home));
        if (_closedTabs.Count > MaxClosedTabs) _closedTabs.RemoveAt(_closedTabs.Count - 1);
    }

    private void ReopenClosedTab(int index = 0)
    {
        if (index < 0 || index >= _closedTabs.Count) return;
        var (paneIndex, path, home) = _closedTabs[index];
        _closedTabs.RemoveAt(index);
        var pane = _panes[paneIndex];
        if (!pane.IsVisible) SetMode(LayoutMode.Quad);
        pane.OpenTab(path, home);
        SetActive(pane);
    }

    // ---- Chuyển tab giữa các panel ----

    private void OnTabDropped(PaneView source, int index, PixelPoint screen)
    {
        var target = _panes.FirstOrDefault(p => p != source && p.IsVisible && ContainsScreenPoint(p, screen));
        if (target is not null) MoveTab(source, index, target);
    }

    private static bool ContainsScreenPoint(Control control, PixelPoint point)
    {
        var topLeft = control.PointToScreen(new Point(0, 0));
        var bottomRight = control.PointToScreen(new Point(control.Bounds.Width, control.Bounds.Height));
        return point.X >= topLeft.X && point.X < bottomRight.X && point.Y >= topLeft.Y && point.Y < bottomRight.Y;
    }

    private void MoveTab(PaneView source, int index, PaneView target)
    {
        if (source == target || source.TabInfo(index) is not { } info) return;
        if (!target.IsVisible) SetMode(LayoutMode.Quad);
        target.OpenTab(info.Path, info.Home);
        source.RemoveTab(index);
        SetActive(target);
    }

    // ---- Sao chép / di chuyển sang panel kia ----

    /// <summary>Panel đích: panel hoạt động trước đó nếu đang hiện, không thì panel cùng hàng, rồi panel hiện bất kỳ.</summary>
    private PaneView? OtherPane()
    {
        var visible = _panes.Where(p => p.IsVisible && p != _active).ToList();
        if (_previous is { } previous && visible.Contains(previous)) return previous;
        return visible.FirstOrDefault(p => p.Index == (_active.Index ^ 1)) ?? visible.FirstOrDefault();
    }

    private void TransferToOtherPane(bool move)
    {
        var verb = move ? "Di chuyển" : "Sao chép";
        try
        {
            var target = OtherPane()
                ?? throw new InvalidOperationException("Cần ít nhất 2 panel đang hiện. Hãy chuyển sang bố cục 2, 3 hoặc 4 khung.");
            var destination = target.CurrentPath;
            if (destination is null || !Directory.Exists(destination))
                throw new InvalidOperationException($"Panel {target.Index + 1} không mở thư mục trên ổ đĩa, nên không thể {verb.ToLowerInvariant()} vào đó.");
            var items = _active.SelectedPaths();
            if (items.Count == 0)
                throw new InvalidOperationException($"Chọn file hoặc thư mục cần {verb.ToLowerInvariant()} trong panel {_active.Index + 1} trước.");
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (ShellTransfer(hwnd, items, destination, move))
                StatusText.Text = $"{verb} {items.Count:N0} mục sang panel {target.Index + 1}";
        }
        catch (Exception ex)
        {
            UpdateService.ShowError(this, ex.Message, $"{verb} sang panel kia");
        }
    }

    // ---- Yêu thích, gần đây, tab đã đóng ----

    private void ToggleFavorite(string? path)
    {
        if (path is null || !Directory.Exists(path))
        {
            UpdateService.ShowError(this, "Chỉ thêm được thư mục trên ổ đĩa vào yêu thích.", "Yêu thích");
            return;
        }
        if (_layout.IsFavorite(path))
            _layout.Favorites.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        else
            _layout.Favorites.Add(path);
        StatusText.Text = _layout.IsFavorite(path) ? "Đã thêm vào yêu thích" : "Đã bỏ khỏi yêu thích";
        LayoutStore.Save(_layout);
    }

    private static string FolderLabel(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : $"{name}    {path}";
    }

    private void ShowFavoritesMenu(PaneView pane, Control anchor)
    {
        SetActive(pane);
        var items = new List<Control>();
        MenuItem Item(string header, Action action, CommandId? command = null, bool enabled = true, string? tip = null)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            if (command is { } id) item.InputGesture = GestureFor(id);
            if (tip is not null) ToolTip.SetTip(item, tip);
            item.Click += (_, _) => action();
            return item;
        }
        void Go(string path)
        {
            if (!Directory.Exists(path)) UpdateService.ShowError(this, $"Không tìm thấy thư mục: {path}", "Yêu thích");
            else if (!pane.Navigate(path)) pane.OpenTab(path, null);
        }

        var current = pane.CurrentPath;
        bool canFavorite = current is not null && Directory.Exists(current);
        items.Add(Item(_layout.IsFavorite(current) ? "Bỏ thư mục này khỏi yêu thích" : "Thêm thư mục này vào yêu thích",
            () => ToggleFavorite(current), CommandId.AddFavorite, canFavorite));
        items.Add(new Separator());
        if (_layout.Favorites.Count == 0)
            items.Add(new MenuItem { Header = "Chưa có thư mục yêu thích", IsEnabled = false });
        foreach (var favorite in _layout.Favorites.ToList())
            items.Add(Item(FolderLabel(favorite), () => Go(favorite), tip: favorite));

        var recent = new MenuItem { Header = "Gần đây", IsEnabled = _layout.RecentFolders.Count > 0 };
        foreach (var path in _layout.RecentFolders.ToList())
            recent.Items.Add(Item(FolderLabel(path), () => Go(path), tip: path));
        var closed = new MenuItem { Header = "Tab đã đóng", IsEnabled = _closedTabs.Count > 0 };
        for (int i = 0; i < _closedTabs.Count; i++)
        {
            int index = i;
            var info = _closedTabs[i];
            closed.Items.Add(Item($"{FolderLabel(info.Path)}    (panel {info.Pane + 1})", () => ReopenClosedTab(index),
                i == 0 ? CommandId.ReopenTab : null, tip: info.Path));
        }
        items.Add(new Separator());
        items.Add(recent);
        items.Add(closed);
        items.Add(new Separator());
        items.Add(Item("Quản lý yêu thích...", () => OpenSettings("Favorites")));
        var flyout = new MenuFlyout { ItemsSource = items, Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.ShowAt(anchor);
    }

    // ---- Workspace ----

    private void Workspace_Click(object? sender, RoutedEventArgs e)
    {
        var items = new List<Control>();
        foreach (var workspace in _layout.Workspaces.ToList())
        {
            var item = new MenuItem { Header = workspace.Name };
            ToolTip.SetTip(item, $"{workspace.TabPaths.Sum(t => t?.Length ?? 0)} tab · {LayoutName(workspace.Mode)}");
            item.Click += (_, _) => ApplyWorkspace(workspace);
            items.Add(item);
        }
        if (items.Count == 0) items.Add(new MenuItem { Header = "Chưa có workspace nào", IsEnabled = false });
        items.Add(new Separator());
        var save = new MenuItem { Header = "Lưu workspace hiện tại...", InputGesture = GestureFor(CommandId.SaveWorkspace) };
        save.Click += (_, _) => _ = SaveWorkspaceAsync();
        items.Add(save);
        var manage = new MenuItem { Header = "Quản lý workspace..." };
        manage.Click += (_, _) => OpenSettings("Workspaces");
        items.Add(manage);
        new MenuFlyout { ItemsSource = items, Placement = PlacementMode.BottomEdgeAlignedRight }.ShowAt(WorkspaceButton);
    }

    internal static string LayoutName(LayoutMode mode) => mode switch
    {
        LayoutMode.Triple => "3 khung",
        LayoutMode.Dual => "2 khung",
        LayoutMode.Single => "1 khung",
        _ => "4 khung",
    };

    private Workspace CaptureWorkspace(string name) => new()
    {
        Name = name,
        Mode = _mode,
        ActivePane = _active.Index,
        TabPaths = [.. _panes.Select(p => p.TabPaths)],
        TabHomes = [.. _panes.Select(p => p.TabHomes)],
        ActiveTabs = [.. _panes.Select(p => p.ActiveTabIndex)],
        ShowTree = [.. _panes.Select(p => p.ShowTree)],
        ShowPreview = [.. _panes.Select(p => p.ShowPreview)],
    };

    private async Task SaveWorkspaceAsync()
    {
        var name = await InputDialog.ShowAsync(this, "Lưu workspace",
            "Tên workspace (lưu toàn bộ tab, thư mục home và bố cục hiện tại):", _layout.Workspaces.Count == 0 ? "Workspace 1" : "");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        int existing = _layout.Workspaces.FindIndex(w => string.Equals(w.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing >= 0 && !UpdateService.Confirm(this, $"Workspace \"{name}\" đã có. Ghi đè bằng bộ tab hiện tại?", "Lưu workspace"))
            return;
        var workspace = CaptureWorkspace(name);
        if (existing >= 0) _layout.Workspaces[existing] = workspace;
        else _layout.Workspaces.Add(workspace);
        LayoutStore.Save(_layout);
        StatusText.Text = $"Đã lưu workspace \"{name}\"";
        _settingsWindow?.RefreshLists();
    }

    internal void ApplyWorkspace(Workspace workspace)
    {
        if (UsesBothSplitters(_mode)) RememberRatios();
        for (int i = 0; i < 4; i++)
            _panes[i].ReplaceTabs(workspace.TabPaths[i] ?? [], workspace.TabHomes[i], workspace.ActiveTabs[i],
                workspace.ShowTree[i], workspace.ShowPreview[i]);
        _mode = workspace.Mode;
        _active = _panes[Math.Clamp(workspace.ActivePane, 0, 3)];
        _previous = null;
        ApplyLayout();
        SetActive(_active);
        _active.FocusView();
        StatusText.Text = $"Workspace: {workspace.Name}";
    }

    // ---- Bảng lệnh ----

    private void CommandPalette_Click(object? sender, RoutedEventArgs e) => OpenCommandPalette();

    private void OpenCommandPalette()
    {
        if (_palette is not null) { _palette.Activate(); return; }
        var entries = new List<CommandPalette.Entry>();
        foreach (var command in CommandCatalog.All)
        {
            var id = command.Id;
            entries.Add(new(command.Title, command.Group, CommandCatalog.GestureText(_layout, id), () => RunCommand(id)));
        }
        foreach (var workspace in _layout.Workspaces.ToList())
            entries.Add(new($"Workspace: {workspace.Name}", "Workspace", LayoutName(workspace.Mode), () => ApplyWorkspace(workspace)));
        foreach (var path in _layout.Favorites.ToList())
            entries.Add(new($"Mở {path}", "Yêu thích", "", () => OpenPathInActive(path)));
        foreach (var path in _layout.RecentFolders.ToList())
            entries.Add(new($"Mở {path}", "Gần đây", "", () => OpenPathInActive(path)));
        _palette = new CommandPalette(entries);
        _palette.Closed += (_, _) => _palette = null;
        _palette.ShowNear(this);
    }

    private void OpenPathInActive(string path)
    {
        if (!Directory.Exists(path)) UpdateService.ShowError(this, $"Không tìm thấy thư mục: {path}", "QuadPane");
        else if (!_active.Navigate(path)) _active.OpenTab(path, null);
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
            if (_layout.CloseToTray)
            {
                e.Cancel = true;
                _settingsWindow?.Close();
                Hide();
                return;
            }
            App.RequestExit();
        }
        _settingsWindow?.Close();
        _updateTimer.Stop();
        _gitTimer.Stop();
        _statusTimer.Stop();
        _gitRefresh?.Cancel();
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        if (_sentMessageHook != IntPtr.Zero) { UnhookWindowsHookEx(_sentMessageHook); _sentMessageHook = IntPtr.Zero; }
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
            _layout.ShowPreview[i] = _panes[i].ShowPreview;
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
