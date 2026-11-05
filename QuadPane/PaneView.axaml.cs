using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using QuadPane.Shell;

namespace QuadPane;

/// <summary>Một panel với các tab Explorer độc lập.</summary>
public partial class PaneView : UserControl
{
    private sealed class PaneTab
    {
        public required ExplorerHost Host { get; init; }
        public required Border Container { get; init; }
        public required TextBlock Label { get; init; }
        public required Button CloseButton { get; init; }
        public required string Id { get; init; }
        public SearchResultsView? Search { get; init; }
        public string? Path { get; set; }
        public string? Display { get; set; }
        public string? HomePath { get; set; }
    }

    private readonly List<PaneTab> _tabs = [];
    private int _selectedIndex;
    private bool _showTree;
    private bool _dark;
    private readonly string _defaultPath;

    public int Index { get; }
    public event Action<PaneView>? Activated;
    public event Action<PaneView>? PathChanged;

    private PaneTab CurrentTab => _tabs[_selectedIndex];
    private ExplorerHost Host => CurrentTab.Host;
    public string? CurrentPath => CurrentTab.Search is null ? CurrentTab.Path : null;
    public string DisplayPath => CurrentTab.Search is { } search ? $"Everything: {search.Query}" : AddressBox.Text ?? "";
    public int ItemCount => CurrentTab.Search?.ResultCount ?? Host.ItemCount;
    public bool ShowTree => _showTree;
    public string?[] TabPaths => _tabs.Where(t => t.Search is null).Select(t => t.Path).ToArray();
    public string?[] TabHomes => _tabs.Where(t => t.Search is null).Select(t => t.HomePath).ToArray();
    public string[] TabIds => _tabs.Where(t => t.Search is null).Select(t => t.Id).ToArray();
    public int ActiveTabIndex => Math.Clamp(_tabs.Take(_selectedIndex).Count(t => t.Search is null), 0, Math.Max(0, TabPaths.Length - 1));

    public int FindTab(string path)
    {
        for (int offset = 0; offset < _tabs.Count; offset++)
        {
            int index = (_selectedIndex + offset) % _tabs.Count;
            if (_tabs[index].Search is not null) continue;
            var current = _tabs[index].Path;
            if (current is null) continue;
            try
            {
                if (Path.IsPathFullyQualified(current) &&
                    string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(current)), path,
                        StringComparison.OrdinalIgnoreCase)) return index;
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (PathTooLongException) { }
        }
        return -1;
    }

    public void ActivateTab(int index) => SelectTab(index);
    public void OpenTab(string path) => AddTab(path, $"QuadPane.Pane{Index}.Tab{Guid.NewGuid():N}", select: true);
    public SearchResultsView OpenSearchTab()
    {
        var search = new SearchResultsView();
        search.OpenFolder += path => OpenTab(path);
        search.ResultsChanged += () => { if (IsCurrentSearch(search)) PathChanged?.Invoke(this); };
        AddTab(_defaultPath, $"QuadPane.Pane{Index}.Tab{Guid.NewGuid():N}", select: true, search: search, focus: false);
        return search;
    }
    public bool IsCurrentSearch(SearchResultsView search) => CurrentTab.Search == search;
    public bool ContainsSearch(SearchResultsView search) => _tabs.Any(t => t.Search == search);
    public void ActivateSearch(SearchResultsView search)
    {
        int index = _tabs.FindIndex(t => t.Search == search);
        if (index >= 0) SelectTab(index, focus: false);
    }
    public void Search(SearchResultsView view, string query)
    {
        var tab = _tabs.FirstOrDefault(t => t.Search == view);
        if (tab is null) return;
        tab.Label.Text = query.Length == 0 ? "Tìm kiếm" : $"Tìm: {query}";
        view.Search(query);
    }

    public PaneView() : this(0, [null], ["QuadPane.Pane0"], 0, false, LayoutStore.DefaultPath(0)) { }
    public PaneView(int index, string? initialPath, bool showTree)
        : this(index, [initialPath], [$"QuadPane.Pane{index}"], 0, showTree, LayoutStore.DefaultPath(index)) { }

    public PaneView(int index, string?[] paths, string[] ids, int selectedIndex, bool showTree,
        string defaultPath, string?[]? homes = null)
    {
        InitializeComponent();
        Index = index;
        _showTree = showTree;
        _defaultPath = defaultPath;
        TreeToggle.IsChecked = showTree;
        for (int i = 0; i < paths.Length; i++)
            AddTab(paths[i], ids[i], select: false, home: homes is not null && i < homes.Length ? homes[i] : null);
        if (_tabs.Count == 0) AddTab(defaultPath, $"QuadPane.Pane{index}", select: false);
        SelectTab(Math.Clamp(selectedIndex, 0, _tabs.Count - 1), focus: false);
        AddHandler(PointerPressedEvent, (_, _) => Activated?.Invoke(this), RoutingStrategies.Tunnel);
    }

    public bool IsActive { set => Card.Classes.Set("active", value); }
    public bool Dark
    {
        set
        {
            _dark = value;
            foreach (var tab in _tabs) tab.Host.Dark = value;
        }
    }

    private void AddTab(string? path, string id, bool select, SearchResultsView? search = null,
        bool focus = true, string? home = null)
    {
        var host = new ExplorerHost
        {
            InitialPath = path,
            PropertyBagName = id,
            ShowTree = _showTree,
            Dark = _dark,
            IsVisible = false,
        };
        var label = new TextBlock
        {
            Text = search is null ? GetTabTitle(path) : "Tìm kiếm",
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            MaxWidth = 130,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        label.Classes.Add("tabTitle");
        var selectButton = new Button { Content = label };
        selectButton.Classes.Add("tabSelect");
        var closeButton = new Button { Content = "×" };
        closeButton.Classes.Add("tabClose");
        ToolTip.SetTip(closeButton, "Đóng tab (Ctrl+W)");
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 1 };
        row.Children.Add(selectButton);
        row.Children.Add(closeButton);
        var container = new Border { Child = row };
        container.Classes.Add("tabItem");
        var tab = new PaneTab { Host = host, Search = search, Container = container, Label = label,
            CloseButton = closeButton, Id = id, Path = path, Display = path, HomePath = home };
        host.Navigated += (display, parsing) =>
        {
            if (!_tabs.Contains(tab)) return;
            tab.Path = parsing;
            tab.Display = display;
            tab.Label.Text = GetTabTitle(display);
            if (tab == CurrentTab)
            {
                AddressBox.Text = display;
                AddressBox.Classes.Remove("error");
                UpdateHomeButtons();
                PathChanged?.Invoke(this);
            }
        };
        host.ItemCountChanged += () => { if (tab == CurrentTab) PathChanged?.Invoke(this); };
        selectButton.Click += (_, _) => SelectTab(_tabs.IndexOf(tab));
        closeButton.Click += (_, _) => CloseTab(tab);
        container.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(container).Properties.IsMiddleButtonPressed)
            {
                CloseTab(tab);
                e.Handled = true;
            }
        };
        _tabs.Add(tab);
        TabStrip.Children.Insert(TabStrip.Children.Count - 1, container);
        HostLayer.Children.Add(host);
        if (search is not null) HostLayer.Children.Add(search);
        UpdateCloseButtons();
        if (select) SelectTab(_tabs.Count - 1, focus);
    }

    private void UpdateCloseButtons()
    {
        bool canClose = _tabs.Count > 1;
        foreach (var tab in _tabs) tab.CloseButton.IsVisible = canClose;
    }

    private static string GetTabTitle(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Tab mới";
        var trimmed = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private void SelectTab(int index, bool focus = true)
    {
        if (index < 0 || index >= _tabs.Count) return;
        if (index == _selectedIndex && _tabs[index].Container.Classes.Contains("selected"))
        {
            Activated?.Invoke(this);
            if (focus) Dispatcher.UIThread.Post(FocusView, DispatcherPriority.Background);
            return;
        }
        int previous = _selectedIndex;
        _selectedIndex = index;
        if (previous != index && previous < _tabs.Count)
        {
            _tabs[previous].Host.IsVisible = false;
            if (_tabs[previous].Search is { } oldSearch) oldSearch.IsVisible = false;
            _tabs[previous].Container.Classes.Set("selected", false);
        }
        var selected = _tabs[index];
        if (selected.Search is { } newSearch) newSearch.IsVisible = true;
        else selected.Host.IsVisible = true;
        selected.Container.Classes.Set("selected", true);
        NavigationBar.IsVisible = CurrentTab.Search is null;
        UpdateHomeButtons();
        AddressBox.Text = CurrentTab.Display ?? "";
        AddressBox.Classes.Remove("error");
        Activated?.Invoke(this);
        PathChanged?.Invoke(this);
        if (focus) Dispatcher.UIThread.Post(() => { if (CurrentTab == selected) FocusView(); }, DispatcherPriority.Background);
        Dispatcher.UIThread.Post(() => selected.Container.BringIntoView(), DispatcherPriority.Background);
    }

    private void CloseTab(PaneTab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;
        if (_tabs.Count == 1)
        {
            if (tab.Search is not null)
            {
                AddTab(_defaultPath, $"QuadPane.Pane{Index}.Tab{Guid.NewGuid():N}", select: false);
                CloseTab(tab);
                return;
            }
            if (!tab.Host.Navigate(_defaultPath))
                tab.Host.Navigate("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");
            return;
        }
        if (tab.Search is null && _tabs.Count(t => t.Search is null) == 1)
            AddTab(_defaultPath, $"QuadPane.Pane{Index}.Tab{Guid.NewGuid():N}", select: false);
        bool wasSelected = index == _selectedIndex;
        _tabs.RemoveAt(index);
        TabStrip.Children.Remove(tab.Container);
        HostLayer.Children.Remove(tab.Host);
        if (tab.Search is { } search)
        {
            search.Stop();
            HostLayer.Children.Remove(search);
        }
        UpdateCloseButtons();
        if (index < _selectedIndex) _selectedIndex--;
        if (wasSelected) SelectTab(Math.Min(index, _tabs.Count - 1));
    }

    public void OpenTab() => AddTab(@"C:\", $"QuadPane.Pane{Index}.Tab{Guid.NewGuid():N}", select: true);
    public void CloseCurrentTab() => CloseTab(CurrentTab);
    public void SelectNextTab(int direction) => SelectTab((_selectedIndex + direction + _tabs.Count) % _tabs.Count);

    public bool Owns(IntPtr hwnd) => _tabs.Any(t => t.Host.Owns(hwnd));
    internal bool TranslateAccelerator(ref MSG msg) => Host.TranslateAccelerator(ref msg);
    public void GoBack() => Host.GoBack();
    public void GoForward() => Host.GoForward();
    public void GoUp() => Host.GoUp();
    public void RefreshView()
    {
        if (CurrentTab.Search is { } search) search.Search(search.Query);
        else Host.RefreshView();
    }
    public void FocusView()
    {
        if (CurrentTab.Search is { } search) search.FocusResults();
        else Host.FocusView();
    }
    public bool Navigate(string path)
    {
        if (CurrentTab.Search is null) return Host.Navigate(path);
        OpenTab(path);
        return true;
    }

    private void UpdateHomeButtons()
    {
        var tab = CurrentTab;
        var path = tab.Search is null ? tab.Path : null;
        bool canPin = false;
        try { canPin = path is not null && Path.IsPathFullyQualified(path); }
        catch (ArgumentException) { }
        PinButton.IsEnabled = canPin;
        PinButton.IsChecked = canPin && tab.HomePath is not null &&
            string.Equals(path, tab.HomePath, StringComparison.OrdinalIgnoreCase);
        HomeButton.IsEnabled = tab.Search is null && tab.HomePath is not null;
        ToolTip.SetTip(PinButton, PinButton.IsChecked == true
            ? "Bỏ ghim thư mục home của tab" : "Ghim thư mục này làm home của tab");
        ToolTip.SetTip(HomeButton, tab.HomePath is null
            ? "Chưa ghim thư mục cho tab này" : $"Về {tab.HomePath}");
    }

    private void Pin_Click(object? sender, RoutedEventArgs e)
    {
        // ponytail: home chỉ nhận thư mục filesystem; dùng PIDL nếu cần ghim thư mục ảo.
        var path = CurrentPath;
        if (path is null || !Directory.Exists(path))
        {
            UpdateHomeButtons();
            ShowActionError("Thư mục này không còn tồn tại.");
            return;
        }
        CurrentTab.HomePath = PinButton.IsChecked == true ? path : null;
        UpdateHomeButtons();
    }

    private void Home_Click(object? sender, RoutedEventArgs e)
    {
        if (CurrentTab.HomePath is not { } home) return;
        if (!Directory.Exists(home) || !Host.Navigate(home))
            ShowActionError($"Không thể mở thư mục home: {home}");
    }

    public void FocusAddress()
    {
        if (CurrentTab.Search is not null) OpenTab();
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    private void AddTab_Click(object? sender, RoutedEventArgs e) => OpenTab();
    private void Claude_Click(object? sender, RoutedEventArgs e) => LaunchTerminal("claude");
    private void Codex_Click(object? sender, RoutedEventArgs e) => LaunchTerminal("codex");
    private void VSCode_Click(object? sender, RoutedEventArgs e) => LaunchTool("code", ".");

    private string FolderPath()
    {
        var path = CurrentPath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            throw new DirectoryNotFoundException("Tab này không trỏ tới thư mục trên ổ đĩa.");
        return path;
    }

    private static string FindTool(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var folder = directory.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(folder)) continue;
            foreach (var extension in new[] { ".exe", ".cmd", ".bat" })
            {
                var file = Path.Combine(folder, name + extension);
                if (File.Exists(file)) return Path.GetFullPath(file);
            }
        }
        throw new FileNotFoundException($"Không tìm thấy lệnh {name} trong PATH. Hãy cài công cụ rồi mở lại QuadPane.");
    }

    private void LaunchTool(string name, string? arguments = null)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(FindTool(name))
            {
                Arguments = arguments ?? "",
                WorkingDirectory = FolderPath(),
                UseShellExecute = true,
            }) ?? throw new InvalidOperationException($"Không thể mở {name}.");
        }
        catch (Exception ex) { ShowActionError(ex.Message); }
    }

    private void LaunchTerminal(string command)
    {
        try
        {
            var folder = FolderPath();
            var executable = FindTool(command);
            var invocation = "& '" + executable.Replace("'", "''") + "'";
            string shell;
            try { shell = FindTool("pwsh"); }
            catch (FileNotFoundException)
            {
                shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe");
            }

            try
            {
                var terminal = new ProcessStartInfo(FindTool("wt"))
                {
                    WorkingDirectory = folder,
                    UseShellExecute = true,
                };
                foreach (var arg in new[] { "-d", folder, shell, "-NoExit", "-Command", invocation })
                    terminal.ArgumentList.Add(arg);
                using var process = Process.Start(terminal) ?? throw new InvalidOperationException("Không thể mở Windows Terminal.");
                return;
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (FileNotFoundException) { }
            catch (InvalidOperationException) { }

            var powershell = new ProcessStartInfo(shell)
            {
                WorkingDirectory = folder,
                UseShellExecute = true,
            };
            foreach (var arg in new[] { "-NoExit", "-Command", invocation })
                powershell.ArgumentList.Add(arg);
            using var fallback = Process.Start(powershell)
                ?? throw new InvalidOperationException("Không thể mở PowerShell.");
        }
        catch (Exception ex) { ShowActionError(ex.Message); }
    }

    private async void CopyPath_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
                ?? throw new InvalidOperationException("Không thể truy cập clipboard.");
            var paths = CurrentTab.Search is { } search
                ? search.SelectedPath is { } selected ? new[] { selected } : []
                : Host.SelectedPaths();
            await clipboard.SetTextAsync(paths.Count > 0 ? string.Join(Environment.NewLine, paths) : FolderPath());
        }
        catch (Exception ex) { ShowActionError(ex.Message); }
    }

    private void ShowActionError(string message) => MessageBoxW(
        TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero,
        message, "QuadPane", 0x10);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint options);

    private void Back_Click(object? sender, RoutedEventArgs e) => GoBack();
    private void Forward_Click(object? sender, RoutedEventArgs e) => GoForward();
    private void Up_Click(object? sender, RoutedEventArgs e) => GoUp();
    private void Refresh_Click(object? sender, RoutedEventArgs e) => RefreshView();
    private void TreeToggle_Changed(object? sender, RoutedEventArgs e)
    {
        _showTree = TreeToggle.IsChecked == true;
        foreach (var tab in _tabs) tab.Host.ShowTree = _showTree;
    }
    private void AddressBox_GotFocus(object? sender, FocusChangedEventArgs e)
    {
        Activated?.Invoke(this);
        Dispatcher.UIThread.Post(() => AddressBox.SelectAll());
    }
    private void AddressBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Host.Navigate(AddressBox.Text ?? "")) FocusView();
            else AddressBox.Classes.Add("error");
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            FocusView();
            e.Handled = true;
        }
        else AddressBox.Classes.Remove("error");
    }
}
