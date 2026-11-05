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
        public string? Path { get; set; }
        public string? Display { get; set; }
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
    public string? CurrentPath => Host.CurrentParsingName ?? CurrentTab.Path;
    public string DisplayPath => AddressBox.Text ?? "";
    public bool ShowTree => _showTree;
    public string?[] TabPaths => _tabs.Select(t => t.Host.CurrentParsingName ?? t.Path).ToArray();
    public string[] TabIds => _tabs.Select(t => t.Id).ToArray();
    public int ActiveTabIndex => _selectedIndex;

    public PaneView() : this(0, [null], ["QuadPane.Pane0"], 0, false, LayoutStore.DefaultPath(0)) { }
    public PaneView(int index, string? initialPath, bool showTree)
        : this(index, [initialPath], [$"QuadPane.Pane{index}"], 0, showTree, LayoutStore.DefaultPath(index)) { }

    public PaneView(int index, string?[] paths, string[] ids, int selectedIndex, bool showTree, string defaultPath)
    {
        InitializeComponent();
        Index = index;
        _showTree = showTree;
        _defaultPath = defaultPath;
        TreeToggle.IsChecked = showTree;
        for (int i = 0; i < paths.Length; i++) AddTab(paths[i], ids[i], select: false);
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

    private void AddTab(string? path, string id, bool select)
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
            Text = GetTabTitle(path),
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
        var tab = new PaneTab { Host = host, Container = container, Label = label, CloseButton = closeButton, Id = id, Path = path, Display = path };
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
                PathChanged?.Invoke(this);
            }
        };
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
        UpdateCloseButtons();
        if (select) SelectTab(_tabs.Count - 1);
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
        _selectedIndex = index;
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool selected = i == index;
            _tabs[i].Host.IsVisible = selected;
            _tabs[i].Container.Classes.Set("selected", selected);
        }
        AddressBox.Text = CurrentTab.Display ?? "";
        AddressBox.Classes.Remove("error");
        Activated?.Invoke(this);
        PathChanged?.Invoke(this);
        if (focus) Dispatcher.UIThread.Post(FocusView, DispatcherPriority.Background);
        Dispatcher.UIThread.Post(() => CurrentTab.Container.BringIntoView(), DispatcherPriority.Background);
    }

    private void CloseTab(PaneTab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;
        if (_tabs.Count == 1)
        {
            if (!tab.Host.Navigate(_defaultPath))
                tab.Host.Navigate("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");
            return;
        }
        bool wasSelected = index == _selectedIndex;
        _tabs.RemoveAt(index);
        TabStrip.Children.Remove(tab.Container);
        HostLayer.Children.Remove(tab.Host);
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
    public void RefreshView() => Host.RefreshView();
    public void FocusView() => Host.FocusView();
    public bool Navigate(string path) => Host.Navigate(path);

    public void FocusAddress()
    {
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
            var paths = Host.SelectedPaths();
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
