using System.Diagnostics;
using System.IO;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Microsoft.Win32;
using QuadPane.Shell;

namespace QuadPane;

/// <summary>
/// Cửa sổ Cài đặt kiểu Windows 11: thanh mục bên trái, các thẻ tuỳ chọn bên phải.
/// Mọi thay đổi áp dụng và lưu ngay, không cần nút OK.
/// </summary>
public partial class SettingsWindow : Window
{
    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly MainWindow? _main;
    private readonly LayoutData _settings;
    private bool _loading;
    private bool _ready;
    private CommandId? _capturing;
    private Button? _captureButton;

    // Constructor không tham số cho designer
    public SettingsWindow() : this(null) { }

    internal SettingsWindow(MainWindow? main)
    {
        InitializeComponent();
        _main = main;
        _settings = main?.Settings ?? new LayoutData();

        var version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        NavVersion.Text = $"Phiên bản {version}";
        AboutVersion.Text = $"Phiên bản {version}";
        UpdateVersion.Text = $"QuadPane {version}";
        ConfigPathText.Text = LayoutStore.FolderPath;
        LogPathText.Text = App.CrashLogPath;
        RepoText.Text = $"github.com/{UpdateService.Repo}";
        BuildShortcutList();
        RefreshLists();
        LoadValues();
        RefreshEnvironment();

        _ready = true;
        NavList.SelectedIndex = 0;
        NavList_SelectionChanged(null, null);
        Opened += (_, _) => ApplyTitleBarTheme();
        Activated += (_, _) => { RefreshEnvironment(); RefreshLists(); };
        ActualThemeVariantChanged += (_, _) => ApplyTitleBarTheme();
        AddHandler(KeyDownEvent, CaptureShortcut, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
    }

    private void LoadValues()
    {
        _loading = true;
        StartupSwitch.IsChecked = IsStartupEnabled();
        CloseActionBox.SelectedIndex = _settings.CloseToTray ? 0 : 1;
        NewTabPathBox.Text = _settings.NewTabPath ?? "";
        ThemeBox.SelectedIndex = (int)_settings.Theme;
        GitBarSwitch.IsChecked = _settings.ShowGitBar;
        ClaudeSwitch.IsChecked = _settings.ShowClaudeButton;
        CodexSwitch.IsChecked = _settings.ShowCodexButton;
        VSCodeSwitch.IsChecked = _settings.ShowVSCodeButton;
        TerminalButtonSwitch.IsChecked = _settings.ShowTerminalButton;
        TerminalSwitch.IsChecked = _settings.UseWindowsTerminal;
        AutoUpdateSwitch.IsChecked = _settings.AutoCheckUpdates;
        _loading = false;
    }

    /// <summary>Trạng thái công cụ ngoài có thể đổi khi người dùng cài thêm: đọc lại mỗi lần cửa sổ được kích hoạt.</summary>
    private void RefreshEnvironment()
    {
        ClaudeStatus.Text = ToolStatus("claude", "Mở Claude Code trong terminal tại thư mục đang xem.");
        CodexStatus.Text = ToolStatus("codex", "Mở Codex trong terminal tại thư mục đang xem.");
        VSCodeStatus.Text = ToolStatus("code", "Mở thư mục đang xem trong VS Code.");
        TerminalStatus.Text = FindTool("wt") is null
            ? "Chưa cài Windows Terminal: sẽ dùng PowerShell."
            : "Tắt để luôn mở bằng cửa sổ PowerShell riêng.";

        bool everything = Process.GetProcessesByName("Everything").Length > 0 ||
                          Process.GetProcessesByName("Everything64").Length > 0;
        EverythingStatus.Text = everything
            ? "Everything đang chạy. Ô tìm kiếm trên thanh tiêu đề đã sẵn sàng."
            : "Everything chưa chạy. Cài và mở Everything để dùng ô tìm kiếm.";
        EverythingDownload.IsVisible = !everything;

        OpenLogButton.IsEnabled = File.Exists(App.CrashLogPath);
        if (UpdateStatus.Text is null or "")
            UpdateStatus.Text = _main?.AvailableUpdateTag is { } tag
                ? $"Có phiên bản mới {tag}."
                : "Bấm Kiểm tra cập nhật để xem có phiên bản mới không.";
    }

    private static string? FindTool(string name)
    {
        try { return PaneView.FindTool(name); }
        catch (FileNotFoundException) { return null; }
    }

    private static string ToolStatus(string command, string description) =>
        FindTool(command) is null ? $"Không tìm thấy lệnh {command} trong PATH. Nút vẫn hiện nếu bật nhưng sẽ báo lỗi khi nhấn." : description;

    // ---- Trang Phím tắt ----

    private void BuildShortcutList()
    {
        ShortcutList.Children.Clear();
        foreach (var group in CommandCatalog.All.GroupBy(c => c.Group))
        {
            ShortcutList.Children.Add(new TextBlock { Text = group.Key, Classes = { "groupHeader" } });
            var card = new StackPanel();
            int i = 0;
            foreach (var command in group)
            {
                var id = command.Id;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Margin = new Avalonia.Thickness(16, 8), ColumnSpacing = 8 };
                row.Children.Add(new TextBlock { Text = command.Title, FontSize = 14, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
                var gestures = CommandCatalog.Gestures(_settings, id);
                var keys = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                if (gestures.Length == 0)
                    keys.Children.Add(new TextBlock { Text = "Chưa gán", FontSize = 12, Classes = { "cardDescription" } });
                foreach (var gesture in gestures)
                    keys.Children.Add(new Border { Classes = { "kbd" }, Child = new TextBlock { Text = gesture.ToString() } });
                Grid.SetColumn(keys, 1);
                row.Children.Add(keys);
                var change = new Button { Content = "Đổi", MinWidth = 64, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
                change.Click += (_, _) => BeginCapture(id, change);
                Grid.SetColumn(change, 2);
                row.Children.Add(change);
                var reset = new Button
                {
                    Content = "\uE777", FontFamily = (Avalonia.Media.FontFamily)this.FindResource("IconFont")!,
                    IsEnabled = _settings.Shortcuts.ContainsKey(id.ToString()),
                };
                ToolTip.SetTip(reset, "Về mặc định: " + string.Join(" / ", CommandCatalog.DefaultGestures(id).Select(g => g.ToString())));
                reset.Click += (_, _) =>
                {
                    CommandCatalog.Reset(_settings, id);
                    ShortcutsChanged(null);
                };
                Grid.SetColumn(reset, 3);
                row.Children.Add(reset);
                var line = new Border { Child = row };
                if (i++ > 0) line.Classes.Add("separated");
                card.Children.Add(line);
            }
            ShortcutList.Children.Add(new Border { Classes = { "card" }, Padding = new Avalonia.Thickness(0), Child = card });
        }
    }

    private void BeginCapture(CommandId id, Button button)
    {
        CancelCapture();
        _capturing = id;
        _captureButton = button;
        button.Content = "Nhấn phím...";
        button.Classes.Add("accent");
        ShortcutMessage.IsVisible = false;
    }

    private void CancelCapture()
    {
        if (_captureButton is { } button)
        {
            button.Content = "Đổi";
            button.Classes.Remove("accent");
        }
        _capturing = null;
        _captureButton = null;
    }

    private void CaptureShortcut(object? sender, KeyEventArgs e)
    {
        if (_capturing is not { } id) return;
        e.Handled = true;
        if (KeyChord.IsModifierKey(e.Key)) return;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) { CancelCapture(); return; }
        if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None)
        {
            CommandCatalog.Assign(_settings, id, null);
            ShortcutsChanged($"Đã bỏ phím tắt của \"{CommandCatalog.Get(id).Title}\".");
            return;
        }
        var chord = KeyChord.Create(e.Key, e.KeyModifiers);
        var removed = CommandCatalog.Assign(_settings, id, chord);
        ShortcutsChanged(removed is { } other
            ? $"{chord} đã được gỡ khỏi \"{CommandCatalog.Get(other).Title}\" và gán cho \"{CommandCatalog.Get(id).Title}\"."
            : null);
    }

    private void ShortcutsChanged(string? message)
    {
        _capturing = null;
        _captureButton = null;
        BuildShortcutList();
        ShortcutMessage.Text = message;
        ShortcutMessage.IsVisible = message is not null;
        _main?.ApplySettings();
    }

    private void ResetAllShortcuts_Click(object? sender, RoutedEventArgs e)
    {
        _settings.Shortcuts.Clear();
        ShortcutsChanged("Đã khôi phục toàn bộ phím tắt mặc định.");
    }

    // ---- Trang Yêu thích và Workspace ----

    /// <summary>Hiện một trang theo Tag (General, Favorites, Workspaces, ...).</summary>
    internal void ShowPage(string page)
    {
        var item = NavList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag as string == page);
        if (item is not null) NavList.SelectedItem = item;
    }

    internal void RefreshLists()
    {
        BuildFavoriteList();
        BuildWorkspaceList();
        RecentStatus.Text = _settings.RecentFolders.Count == 0
            ? "Chưa có thư mục nào."
            : $"{_settings.RecentFolders.Count} thư mục, hiện trong menu ☆ và bảng lệnh.";
        ClearRecentButton.IsEnabled = _settings.RecentFolders.Count > 0;
    }

    private Button IconButton(string glyph, string tip, Action action, bool enabled = true)
    {
        var button = new Button
        {
            Content = glyph, FontFamily = (Avalonia.Media.FontFamily)this.FindResource("IconFont")!,
            IsEnabled = enabled, Width = 34, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private static Border ListRow(int index, string title, string description, params Control[] actions)
    {
        var text = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, Classes = { "cardTitle" } });
        text.Children.Add(new TextBlock { Text = description, Classes = { "cardDescription" } });
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
        buttons.Children.AddRange(actions);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Avalonia.Thickness(16, 10) };
        Grid.SetColumn(buttons, 1);
        row.Children.Add(text);
        row.Children.Add(buttons);
        var line = new Border { Child = row };
        if (index > 0) line.Classes.Add("separated");
        return line;
    }

    private static Border EmptyRow(string text) => new()
    {
        Padding = new Avalonia.Thickness(16, 14),
        Child = new TextBlock { Text = text, Classes = { "cardDescription" } },
    };

    private void BuildFavoriteList()
    {
        FavoriteList.Children.Clear();
        var favorites = _settings.Favorites;
        if (favorites.Count == 0) FavoriteList.Children.Add(EmptyRow("Chưa có thư mục yêu thích."));
        for (int i = 0; i < favorites.Count; i++)
        {
            int index = i;
            var path = favorites[i];
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            FavoriteList.Children.Add(ListRow(i, string.IsNullOrEmpty(name) ? path : name,
                Directory.Exists(path) ? path : $"{path} (không còn tồn tại)",
                IconButton("\uE74A", "Chuyển lên", () => MoveFavorite(index, -1), index > 0),
                IconButton("\uE74B", "Chuyển xuống", () => MoveFavorite(index, 1), index < favorites.Count - 1),
                IconButton("\uE74D", "Bỏ khỏi yêu thích", () => { favorites.RemoveAt(index); ListsChanged(); })));
        }
    }

    private void MoveFavorite(int index, int direction)
    {
        var favorites = _settings.Favorites;
        (favorites[index], favorites[index + direction]) = (favorites[index + direction], favorites[index]);
        ListsChanged();
    }

    private void ListsChanged()
    {
        RefreshLists();
        LayoutStore.Save(_settings);
    }

    private async void AddFavorite_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Chọn thư mục yêu thích",
            AllowMultiple = true,
        });
        foreach (var folder in folders)
            if (folder.TryGetLocalPath() is { } path && !_settings.IsFavorite(path))
                _settings.Favorites.Add(path);
        ListsChanged();
    }

    private void ClearRecent_Click(object? sender, RoutedEventArgs e)
    {
        _settings.RecentFolders.Clear();
        ListsChanged();
    }

    private void BuildWorkspaceList()
    {
        WorkspaceList.Children.Clear();
        var workspaces = _settings.Workspaces;
        if (workspaces.Count == 0) WorkspaceList.Children.Add(EmptyRow("Chưa có workspace nào. Bấm \"Lưu bộ tab hiện tại\" để tạo."));
        for (int i = 0; i < workspaces.Count; i++)
        {
            var workspace = workspaces[i];
            int tabs = workspace.TabPaths.Sum(t => t?.Length ?? 0);
            var open = new Button { Content = "Mở" };
            open.Click += (_, _) => _main?.ApplyWorkspace(workspace);
            WorkspaceList.Children.Add(ListRow(i, workspace.Name, $"{tabs} tab · {MainWindow.LayoutName(workspace.Mode)}",
                open,
                IconButton("\uE8AC", "Đổi tên", () => _ = RenameWorkspaceAsync(workspace)),
                IconButton("\uE74D", "Xoá workspace", () =>
                {
                    if (!UpdateService.Confirm(this, $"Xoá workspace \"{workspace.Name}\"?", "Workspace")) return;
                    workspaces.Remove(workspace);
                    ListsChanged();
                })));
        }
    }

    private async Task RenameWorkspaceAsync(Workspace workspace)
    {
        var name = await InputDialog.ShowAsync(this, "Đổi tên workspace", "Tên mới:", workspace.Name, "Đổi tên");
        if (string.IsNullOrWhiteSpace(name) || name == workspace.Name) return;
        if (_settings.Workspaces.Any(w => w != workspace && string.Equals(w.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            UpdateService.ShowError(this, $"Đã có workspace tên \"{name}\". Hãy chọn tên khác.", "Workspace");
            return;
        }
        workspace.Name = name;
        ListsChanged();
    }

    private void SaveWorkspace_Click(object? sender, RoutedEventArgs e) => _main?.RunCommand(CommandId.SaveWorkspace);

    private void ApplyTitleBarTheme() =>
        ShellTheme.ApplyTitleBar(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, ActualThemeVariant == ThemeVariant.Dark);

    /// <summary>Đồng bộ ô Chủ đề khi theme được đổi bằng nút trên thanh tiêu đề.</summary>
    internal void SyncTheme()
    {
        _loading = true;
        ThemeBox.SelectedIndex = (int)_settings.Theme;
        _loading = false;
    }

    private void NavList_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
    {
        // AlwaysSelected chọn mục đầu ngay khi nạp XAML, lúc các trang chưa được gán
        if (!_ready) return;
        CancelCapture();
        if (NavList.SelectedItem is not ListBoxItem { Tag: string page } item) return;
        PageTitle.Text = (item.Content as StackPanel)?.Children.OfType<TextBlock>().LastOrDefault()?.Text;
        GeneralPage.IsVisible = page == "General";
        AppearancePage.IsVisible = page == "Appearance";
        ToolsPage.IsVisible = page == "Tools";
        SearchPage.IsVisible = page == "Search";
        FavoritesPage.IsVisible = page == "Favorites";
        WorkspacesPage.IsVisible = page == "Workspaces";
        ShortcutsPage.IsVisible = page == "Shortcuts";
        UpdatePage.IsVisible = page == "Update";
        AboutPage.IsVisible = page == "About";
    }

    // ---- Lưu tuỳ chọn ----

    private void Setting_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ShowGitBar = GitBarSwitch.IsChecked == true;
        _settings.ShowClaudeButton = ClaudeSwitch.IsChecked == true;
        _settings.ShowCodexButton = CodexSwitch.IsChecked == true;
        _settings.ShowVSCodeButton = VSCodeSwitch.IsChecked == true;
        _settings.ShowTerminalButton = TerminalButtonSwitch.IsChecked == true;
        _settings.UseWindowsTerminal = TerminalSwitch.IsChecked == true;
        _settings.AutoCheckUpdates = AutoUpdateSwitch.IsChecked == true;
        _main?.ApplySettings();
    }

    private void CloseActionBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || CloseActionBox.SelectedIndex < 0) return;
        _settings.CloseToTray = CloseActionBox.SelectedIndex == 0;
        _main?.ApplySettings();
    }

    private void ThemeBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedIndex < 0) return;
        _main?.SetTheme((ThemeMode)ThemeBox.SelectedIndex);
        _main?.ApplySettings();
    }

    // ---- Thư mục cho tab mới ----

    private void CommitNewTabPath()
    {
        var text = NewTabPathBox.Text?.Trim().Trim('"') ?? "";
        if (text.Length > 0 && !Directory.Exists(text))
        {
            NewTabError.Text = $"Không tìm thấy thư mục \"{text}\". Kiểm tra lại đường dẫn hoặc bấm Chọn thư mục.";
            NewTabError.IsVisible = true;
            return;
        }
        NewTabError.IsVisible = false;
        var value = text.Length == 0 ? null : Path.GetFullPath(text);
        if (value == _settings.NewTabPath) return;
        _settings.NewTabPath = value;
        NewTabPathBox.Text = value ?? "";
        _main?.ApplySettings();
    }

    private void NewTabPathBox_LostFocus(object? sender, RoutedEventArgs e) => CommitNewTabPath();

    private void NewTabPathBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitNewTabPath();
        e.Handled = true;
    }

    private async void BrowseNewTab_Click(object? sender, RoutedEventArgs e)
    {
        var start = _settings.EffectiveNewTabPath;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Chọn thư mục cho tab mới",
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(start),
        });
        if (folders is [var folder] && folder.TryGetLocalPath() is { } path)
        {
            NewTabPathBox.Text = path;
            CommitNewTabPath();
        }
    }

    private void ResetNewTab_Click(object? sender, RoutedEventArgs e)
    {
        NewTabPathBox.Text = "";
        CommitNewTabPath();
    }

    // ---- Khởi động cùng Windows ----

    private static bool IsStartupEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey);
        return key?.GetValue("QuadPane") is string value && value == $"\"{Environment.ProcessPath}\"";
    }

    private void StartupSwitch_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading || !OperatingSystem.IsWindows()) return;
        var enabled = StartupSwitch.IsChecked == true;
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
            _loading = true;
            StartupSwitch.IsChecked = !enabled;
            _loading = false;
            UpdateService.ShowError(this, ex.Message, "Cài đặt QuadPane");
        }
    }

    // ---- Cập nhật ----

    private async void CheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Đang kiểm tra...";
        try { UpdateStatus.Text = await _main.CheckForUpdatesNowAsync(); }
        finally { CheckUpdateButton.IsEnabled = true; }
    }

    // ---- Liên kết ----

    private void Open(string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UpdateService.ShowError(this, $"Không mở được {target}: {ex.Message}", "Cài đặt QuadPane");
        }
    }

    private void Releases_Click(object? sender, RoutedEventArgs e) => Open($"https://github.com/{UpdateService.Repo}/releases");
    private void Repo_Click(object? sender, RoutedEventArgs e) => Open($"https://github.com/{UpdateService.Repo}");
    private void EverythingDownload_Click(object? sender, RoutedEventArgs e) => Open("https://www.voidtools.com/downloads/");

    private void OpenConfig_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(LayoutStore.FolderPath);
        Open(LayoutStore.FolderPath);
    }

    private void OpenLog_Click(object? sender, RoutedEventArgs e)
    {
        if (File.Exists(App.CrashLogPath)) Open(App.CrashLogPath);
    }
}
