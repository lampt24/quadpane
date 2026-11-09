using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
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
        public required TextBlock PinIcon { get; init; }
        public required string Id { get; init; }
        public SearchResultsView? Search { get; init; }
        public string? Path { get; set; }
        public string? Display { get; set; }
        public string? HomePath { get; set; }
        /// <summary>Tab ghim: luôn nằm đầu dải tab, không đóng bằng nút ×, chuột giữa hay Ctrl+W.</summary>
        public bool IsPinned { get; set; }
    }

    private readonly List<PaneTab> _tabs = [];
    private int _selectedIndex;
    private bool _showTree;
    private bool _dark;
    private readonly string _defaultPath;
    private string _newTabPath = @"C:\";
    private bool _useWindowsTerminal = true;
    private bool _showPreview;
    private bool _syncingFilter;
    private readonly DispatcherTimer _filterTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private PaneTab? _dragTab;
    private Avalonia.Point _dragStart;
    private bool _dragging;
    /// <summary>Vị trí tab sẽ chuyển tới khi thả trong dải tab của chính panel; -1 khi con trỏ ở ngoài dải tab.</summary>
    private int _dropIndex = -1;

    public int Index { get; }
    public event Action<PaneView>? Activated;
    public event Action<PaneView>? PathChanged;
    /// <summary>Người dùng đóng một tab thư mục: (panel, đường dẫn, home).</summary>
    public event Action<PaneView, string, string?>? TabClosed;
    /// <summary>Đã mở một thư mục trên ổ đĩa (dùng cho danh sách gần đây).</summary>
    public event Action<PaneView, string>? FolderVisited;
    public event Action<PaneView, Control>? FavoritesRequested;
    /// <summary>Thả tab sau khi kéo: (panel nguồn, chỉ số tab, điểm thả trên màn hình).</summary>
    public event Action<PaneView, int, Avalonia.PixelPoint>? TabDropped;
    /// <summary>Chọn "Chuyển sang panel" trong menu tab: (panel nguồn, chỉ số tab, panel đích).</summary>
    public event Action<PaneView, int, int>? TabMoveRequested;

    private PaneTab CurrentTab => _tabs[_selectedIndex];
    private ExplorerHost Host => CurrentTab.Host;
    public string? CurrentPath => CurrentTab.Search is null ? CurrentTab.Path : null;
    public string DisplayPath => CurrentTab.Search is { } search ? $"Everything: {search.Query}" : AddressBox.Text ?? "";
    public int ItemCount => CurrentTab.Search?.ResultCount ?? Host.ItemCount;
    public bool ShowTree => _showTree;
    public bool ShowPreview => _showPreview;
    public int TabCount => _tabs.Count;
    public string?[] TabPaths => _tabs.Where(t => t.Search is null).Select(t => t.Path).ToArray();
    public string?[] TabHomes => _tabs.Where(t => t.Search is null).Select(t => t.HomePath).ToArray();
    public string[] TabIds => _tabs.Where(t => t.Search is null).Select(t => t.Id).ToArray();
    public bool[] TabPinned => _tabs.Where(t => t.Search is null).Select(t => t.IsPinned).ToArray();
    private int PinnedCount => _tabs.Count(t => t.IsPinned);
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
        string defaultPath, string?[]? homes = null, bool showPreview = false, bool[]? pinned = null)
    {
        InitializeComponent();
        Index = index;
        _showTree = showTree;
        _showPreview = showPreview;
        _defaultPath = defaultPath;
        TreeToggle.IsChecked = showTree;
        PreviewToggle.IsChecked = showPreview;
        _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); ApplyFilterText(); };
        for (int i = 0; i < paths.Length; i++)
            AddTab(paths[i], ids[i], select: false, home: homes is not null && i < homes.Length ? homes[i] : null,
                pinned: pinned is not null && i < pinned.Length && pinned[i]);
        if (_tabs.Count == 0) AddTab(defaultPath, $"QuadPane.Pane{index}", select: false);
        SelectTab(Math.Clamp(selectedIndex, 0, _tabs.Count - 1), focus: false);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            Activated?.Invoke(this);
            var buttons = e.GetCurrentPoint(this).Properties;
            if (buttons.IsXButton1Pressed) { GoBack(); e.Handled = true; }
            else if (buttons.IsXButton2Pressed) { GoForward(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }

    public bool IsActive
    {
        set
        {
            Card.Classes.Set("active", value);
            ZIndex = value ? 1 : 0;
        }
    }
    public bool Dark
    {
        set
        {
            _dark = value;
            foreach (var tab in _tabs) tab.Host.Dark = value;
        }
    }

    private void AddTab(string? path, string id, bool select, SearchResultsView? search = null,
        bool focus = true, string? home = null, int insertAt = -1, bool pinned = false)
    {
        pinned &= search is null;
        var host = new ExplorerHost
        {
            InitialPath = path,
            PropertyBagName = id,
            ShowTree = _showTree,
            ShowPreview = _showPreview,
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
        var selectButton = new Button();
        selectButton.Classes.Add("tabSelect");
        var closeButton = new Button
        {
            Content = new Avalonia.Controls.Shapes.Path
            {
                Data = Avalonia.Media.Geometry.Parse("M2 2L10 10 M10 2L2 10"),
            },
        };
        closeButton.Classes.Add("tabClose");
        ToolTip.SetTip(closeButton, "Đóng tab (Ctrl+W)");
        var pinIcon = new TextBlock
        {
            Text = "\uE718",
            IsVisible = pinned,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        pinIcon.Classes.Add("tabPin");
        var selectContent = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 5 };
        selectContent.Children.Add(pinIcon);
        selectContent.Children.Add(label);
        selectButton.Content = selectContent;
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 1 };
        row.Children.Add(selectButton);
        row.Children.Add(closeButton);
        var container = new Border { Child = row };
        container.Classes.Add("tabItem");
        var tab = new PaneTab { Host = host, Search = search, Container = container, Label = label,
            CloseButton = closeButton, PinIcon = pinIcon, Id = id, Path = path, Display = path, HomePath = home,
            IsPinned = pinned };
        container.Classes.Set("pinned", pinned);
        host.Navigated += (display, parsing) =>
        {
            if (!_tabs.Contains(tab)) return;
            tab.Path = parsing;
            tab.Display = display;
            tab.Label.Text = GetTabTitle(display);
            if (tab.Host.FilterText.Length > 0) tab.Host.SetFilter("");
            if (IsFileSystemFolder(parsing)) FolderVisited?.Invoke(this, parsing);
            if (tab == CurrentTab)
            {
                SyncFilterBox();
                AddressBox.Text = display;
                AddressBox.Classes.Remove("error");
                UpdateHomeButtons();
                PathChanged?.Invoke(this);
            }
        };
        host.ItemCountChanged += () => { if (tab == CurrentTab) PathChanged?.Invoke(this); };
        host.HistoryChanged += () => { if (_tabs.Contains(tab) && tab == CurrentTab) UpdateHomeButtons(); };
        selectButton.Click += (_, _) => SelectTab(_tabs.IndexOf(tab));
        closeButton.Click += (_, _) => CloseTab(tab);
        AttachTabDrag(tab, selectButton);
        container.ContextRequested += (_, e) =>
        {
            BuildTabMenu(tab).Open(container);
            e.Handled = true;
        };
        container.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(container).Properties.IsMiddleButtonPressed)
            {
                if (!tab.IsPinned) CloseTab(tab);
                e.Handled = true;
            }
        };
        int position = insertAt < 0 || insertAt > _tabs.Count ? _tabs.Count : insertAt;
        // Tab ghim luôn đứng trước mọi tab thường
        int pinnedCount = PinnedCount;
        position = pinned ? Math.Min(position, pinnedCount) : Math.Max(position, pinnedCount);
        if (position <= _selectedIndex && position < _tabs.Count) _selectedIndex++;
        _tabs.Insert(position, tab);
        TabStrip.Children.Insert(position, container);
        HostLayer.Children.Add(host);
        if (search is not null) HostLayer.Children.Add(search);
        UpdateCloseButtons();
        if (select) SelectTab(position, focus);
    }

    private void UpdateCloseButtons()
    {
        bool canClose = _tabs.Count > 1;
        foreach (var tab in _tabs) tab.CloseButton.IsVisible = canClose && !tab.IsPinned;
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
        SyncFilterBox();
        UpdateHomeButtons();
        AddressBox.Text = CurrentTab.Display ?? "";
        AddressBox.Classes.Remove("error");
        Activated?.Invoke(this);
        PathChanged?.Invoke(this);
        if (focus) Dispatcher.UIThread.Post(() => { if (CurrentTab == selected) FocusView(); }, DispatcherPriority.Background);
        Dispatcher.UIThread.Post(() => selected.Container.BringIntoView(), DispatcherPriority.Background);
    }

    private void CloseTab(PaneTab tab, bool remember = true)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;
        if (remember && tab.Search is null && tab.Path is { } closedPath)
            TabClosed?.Invoke(this, closedPath, tab.HomePath);
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

    /// <summary>Áp dụng các tuỳ chọn trong cửa sổ Cài đặt.</summary>
    public void ApplySettings(LayoutData settings)
    {
        _newTabPath = settings.EffectiveNewTabPath;
        _useWindowsTerminal = settings.UseWindowsTerminal;
        ClaudeButton.IsVisible = settings.ShowClaudeButton;
        CodexButton.IsVisible = settings.ShowCodexButton;
        VSCodeButton.IsVisible = settings.ShowVSCodeButton;
        TerminalButton.IsVisible = settings.ShowTerminalButton;
    }

    private static string NewTabId(int pane) => $"QuadPane.Pane{pane}.Tab{Guid.NewGuid():N}";

    private static bool IsFileSystemFolder(string? path)
    {
        try { return path is not null && Path.IsPathFullyQualified(path) && !path.StartsWith("::", StringComparison.Ordinal); }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Mở thư mục trong tab mới (dùng cho mở lại tab, yêu thích, chuyển tab giữa các panel).</summary>
    public void OpenTab(string path, string? home, bool select = true, bool pinned = false) =>
        AddTab(path, NewTabId(Index), select, home: home, pinned: pinned);

    public void DuplicateCurrentTab()
    {
        var tab = CurrentTab;
        if (tab.Search is not null) return;
        AddTab(tab.Path, NewTabId(Index), select: true, home: tab.HomePath, insertAt: _selectedIndex + 1);
    }

    /// <summary>Đường dẫn và home của tab; null với tab kết quả tìm kiếm.</summary>
    public (string Path, string? Home, bool Pinned)? TabInfo(int index) =>
        index >= 0 && index < _tabs.Count && _tabs[index] is { Search: null, Path: { } path } tab
            ? (path, tab.HomePath, tab.IsPinned) : null;

    /// <summary>Gỡ tab khỏi panel mà không ghi vào danh sách tab đã đóng (khi chuyển sang panel khác).</summary>
    public void RemoveTab(int index)
    {
        if (index >= 0 && index < _tabs.Count) CloseTab(_tabs[index], remember: false);
    }

    /// <summary>Thay toàn bộ tab của panel (khi chuyển workspace).</summary>
    public void ReplaceTabs(string?[] paths, string?[]? homes, int selectedIndex, bool showTree, bool showPreview,
        bool[]? pinned = null)
    {
        foreach (var tab in _tabs)
        {
            TabStrip.Children.Remove(tab.Container);
            HostLayer.Children.Remove(tab.Host);
            if (tab.Search is { } search)
            {
                search.Stop();
                HostLayer.Children.Remove(search);
            }
        }
        _tabs.Clear();
        _selectedIndex = 0;
        _showTree = showTree;
        _showPreview = showPreview;
        TreeToggle.IsChecked = showTree;
        PreviewToggle.IsChecked = showPreview;
        for (int i = 0; i < paths.Length; i++)
            if (!string.IsNullOrWhiteSpace(paths[i]))
                AddTab(paths[i], NewTabId(Index), select: false, home: homes is not null && i < homes.Length ? homes[i] : null,
                    pinned: pinned is not null && i < pinned.Length && pinned[i]);
        if (_tabs.Count == 0) AddTab(_defaultPath, NewTabId(Index), select: false);
        SelectTab(Math.Clamp(selectedIndex, 0, _tabs.Count - 1), focus: false);
    }

    public IReadOnlyList<string> SelectedPaths() => CurrentTab.Search is { } search
        ? search.SelectedPath is { } selected ? [selected] : []
        : Host.SelectedPaths();

    public void ShowFavorites() => FavoritesRequested?.Invoke(this, NavigationBar.IsVisible ? FavoritesButton : TabStrip);
    public void ToggleTree() { if (CurrentTab.Search is null) TreeToggle.IsChecked = !_showTree; }
    public void TogglePreview() { if (CurrentTab.Search is null) PreviewToggle.IsChecked = !_showPreview; }
    public void OpenTerminal() => LaunchTerminal(null);

    // ---- Lọc nhanh ----

    public void FocusFilter()
    {
        if (CurrentTab.Search is not null) return;
        FilterBox.Focus();
        FilterBox.SelectAll();
    }

    private void SyncFilterBox()
    {
        _syncingFilter = true;
        _filterTimer.Stop();
        FilterBox.Text = CurrentTab.Search is null ? Host.FilterText : "";
        FilterBox.Classes.Set("active", !string.IsNullOrEmpty(FilterBox.Text));
        _syncingFilter = false;
    }

    private void ApplyFilterText()
    {
        if (CurrentTab.Search is not null) return;
        var text = FilterBox.Text ?? "";
        FilterBox.Classes.Set("active", text.Trim().Length > 0);
        if (!Host.SetFilter(text))
            ShowActionError("Explorer của Windows trên máy này không hỗ trợ lọc trong danh sách file.");
    }

    private void FilterBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncingFilter) return;
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void FilterBox_GotFocus(object? sender, FocusChangedEventArgs e) => Activated?.Invoke(this);

    private void FilterBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            FilterBox.Text = "";
            _filterTimer.Stop();
            ApplyFilterText();
            FocusView();
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Down)
        {
            _filterTimer.Stop();
            ApplyFilterText();
            FocusView();
            e.Handled = true;
        }
    }

    // ---- Menu và kéo thả tab ----

    private ContextMenu BuildTabMenu(PaneTab tab)
    {
        var items = new List<Control>();
        MenuItem Item(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }
        bool isFolder = tab.Search is null;
        int index = _tabs.IndexOf(tab);
        var (zoneStart, zoneEnd) = Zone(tab);
        items.Add(Item(tab.IsPinned ? "Bỏ ghim tab" : "Ghim tab", () => SetPinned(tab, !tab.IsPinned), isFolder));
        items.Add(Item("Nhân bản tab", () => { SelectTab(_tabs.IndexOf(tab), focus: false); DuplicateCurrentTab(); }, isFolder));
        items.Add(Item("Chuyển tab sang trái", () => MoveTabTo(tab, _tabs.IndexOf(tab) - 1), index > zoneStart));
        items.Add(Item("Chuyển tab sang phải", () => MoveTabTo(tab, _tabs.IndexOf(tab) + 1), index < zoneEnd));
        var move = new MenuItem { Header = "Chuyển sang panel", IsEnabled = isFolder };
        for (int i = 0; i < 4; i++)
        {
            if (i == Index) continue;
            int target = i;
            move.Items.Add(Item($"Panel {i + 1}", () => TabMoveRequested?.Invoke(this, _tabs.IndexOf(tab), target)));
        }
        items.Add(move);
        items.Add(new Separator());
        items.Add(Item("Đóng các tab khác", () =>
        {
            foreach (var other in _tabs.Where(t => t != tab && !t.IsPinned).ToList()) CloseTab(other);
            SelectTab(_tabs.IndexOf(tab));
        }, _tabs.Any(t => t != tab && !t.IsPinned)));
        items.Add(Item("Đóng các tab bên phải", () =>
        {
            foreach (var other in _tabs.Skip(_tabs.IndexOf(tab) + 1).Where(t => !t.IsPinned).ToList()) CloseTab(other);
            SelectTab(_tabs.IndexOf(tab));
        }, _tabs.Skip(index + 1).Any(t => !t.IsPinned)));
        items.Add(Item("Đóng tab", () => CloseTab(tab)));
        return new ContextMenu { ItemsSource = items };
    }

    private void AttachTabDrag(PaneTab tab, Button button)
    {
        button.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _dragTab = tab;
            _dragStart = e.GetPosition(this);
            _dragging = false;
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        button.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (_dragTab != tab) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { EndTabDrag(); return; }
            var point = e.GetPosition(this);
            if (!_dragging)
            {
                if (Math.Abs(point.X - _dragStart.X) + Math.Abs(point.Y - _dragStart.Y) < 10) return;
                _dragging = true;
                button.Cursor = new Cursor(StandardCursorType.DragMove);
                tab.Container.Opacity = 0.55;
            }
            UpdateDropIndicator(tab, e.GetPosition(TabStrip));
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        button.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (_dragTab != tab) return;
            bool dropped = _dragging;
            var screen = this.PointToScreen(e.GetPosition(this));
            int reorder = dropped ? DropIndex(tab, e.GetPosition(TabStrip)) : -1;
            EndTabDrag();
            if (reorder >= 0) Dispatcher.UIThread.Post(() => MoveTabTo(tab, reorder));
            else if (dropped) TabDropped?.Invoke(this, _tabs.IndexOf(tab), screen);
        }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        // Dự phòng: nếu mất capture khi chuột đang ở trên cửa sổ Explorer nhúng (Win32), lấy vị trí thả từ con trỏ
        button.AddHandler(PointerCaptureLostEvent, (_, _) =>
        {
            if (_dragTab != tab) return;
            bool dropped = _dragging;
            EndTabDrag();
            if (!dropped || !NativeMethods.GetCursorPos(out var cursor)) return;
            var screen = new PixelPoint(cursor.X, cursor.Y);
            int reorder = DropIndex(tab, TabStrip.PointToClient(screen));
            if (reorder >= 0) Dispatcher.UIThread.Post(() => MoveTabTo(tab, reorder));
            else TabDropped?.Invoke(this, _tabs.IndexOf(tab), screen);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void EndTabDrag()
    {
        ClearDropIndicator();
        if (_dragTab is { } tab)
        {
            tab.Container.Opacity = 1;
            if (tab.Container.Child is StackPanel { Children: [Button select, ..] }) select.Cursor = null;
        }
        _dragTab = null;
        _dragging = false;
    }

    // ---- Sắp xếp và ghim tab ----

    /// <summary>Khoảng chỉ số tab được phép đứng: tab ghim nằm trong nhóm ghim, tab thường sau nhóm ghim.</summary>
    private (int Start, int End) Zone(PaneTab tab)
    {
        int pinnedCount = PinnedCount;
        return tab.IsPinned ? (0, pinnedCount - 1) : (pinnedCount, _tabs.Count - 1);
    }

    /// <summary>Chuyển tab tới chỉ số mới trong cùng panel (giới hạn trong nhóm của nó).</summary>
    private void MoveTabTo(PaneTab tab, int newIndex)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;
        var (start, end) = Zone(tab);
        newIndex = Math.Clamp(newIndex, start, end);
        if (newIndex == index) return;
        var selected = CurrentTab;
        _tabs.RemoveAt(index);
        _tabs.Insert(newIndex, tab);
        TabStrip.Children.Remove(tab.Container);
        TabStrip.Children.Insert(newIndex, tab.Container);
        _selectedIndex = _tabs.IndexOf(selected);
        Dispatcher.UIThread.Post(() => tab.Container.BringIntoView(), DispatcherPriority.Background);
    }

    private void SetPinned(PaneTab tab, bool pinned)
    {
        if (tab.Search is not null || tab.IsPinned == pinned || !_tabs.Contains(tab)) return;
        // Ghim: đưa về cuối nhóm ghim; bỏ ghim: đưa về đầu nhóm tab thường.
        int pinnedCount = PinnedCount;
        tab.IsPinned = pinned;
        tab.PinIcon.IsVisible = pinned;
        tab.Container.Classes.Set("pinned", pinned);
        MoveTabTo(tab, pinned ? pinnedCount : pinnedCount - 1);
        UpdateCloseButtons();
    }

    /// <summary>Chỉ số đích khi thả tab tại <paramref name="point"/> (toạ độ của dải tab); -1 nếu thả ra ngoài dải tab.</summary>
    private int DropIndex(PaneTab tab, Avalonia.Point point)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0 || point.Y < -8 || point.Y > TabStrip.Bounds.Height + 8 ||
            point.X < -8 || point.X > TabStrip.Bounds.Width + 8) return -1;
        int before = _tabs.Count;
        for (int i = 0; i < _tabs.Count; i++)
        {
            var bounds = _tabs[i].Container.Bounds;
            if (point.X < bounds.X + bounds.Width / 2) { before = i; break; }
        }
        int target = before > index ? before - 1 : before;
        var (start, end) = Zone(tab);
        return Math.Clamp(target, start, end);
    }

    private void UpdateDropIndicator(PaneTab tab, Avalonia.Point point)
    {
        int target = DropIndex(tab, point);
        int index = _tabs.IndexOf(tab);
        if (target == _dropIndex) return;
        ClearDropIndicator();
        _dropIndex = target;
        if (target < 0 || target == index) return;
        _tabs[target].Container.Classes.Add(target < index ? "dropBefore" : "dropAfter");
    }

    private void ClearDropIndicator()
    {
        foreach (var t in _tabs)
        {
            t.Container.Classes.Remove("dropBefore");
            t.Container.Classes.Remove("dropAfter");
        }
        _dropIndex = -1;
    }

    public void OpenTab() => AddTab(_newTabPath, NewTabId(Index), select: true);
    public void CloseCurrentTab() { if (!CurrentTab.IsPinned) CloseTab(CurrentTab); }
    public void MoveCurrentTab(int direction) => MoveTabTo(CurrentTab, _selectedIndex + direction);
    public void TogglePinCurrentTab() => SetPinned(CurrentTab, !CurrentTab.IsPinned);
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
        BackButton.IsEnabled = tab.Search is null && tab.Host.CanGoBack;
        ForwardButton.IsEnabled = tab.Search is null && tab.Host.CanGoForward;
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
    private void Terminal_Click(object? sender, RoutedEventArgs e) => LaunchTerminal(null);
    private void Favorites_Click(object? sender, RoutedEventArgs e) => FavoritesRequested?.Invoke(this, FavoritesButton);

    private string FolderPath()
    {
        var path = CurrentPath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            throw new DirectoryNotFoundException("Tab này không trỏ tới thư mục trên ổ đĩa.");
        return path;
    }

    internal static string FindTool(string name)
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

    /// <summary>Mở terminal tại thư mục đang xem; <paramref name="command"/> null thì chỉ mở shell.</summary>
    private void LaunchTerminal(string? command)
    {
        try
        {
            var folder = FolderPath();
            string? invocation = command is null ? null : "& '" + FindTool(command).Replace("'", "''") + "'";
            string shell;
            try { shell = FindTool("pwsh"); }
            catch (FileNotFoundException)
            {
                shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe");
            }

            if (_useWindowsTerminal) try
            {
                var terminal = new ProcessStartInfo(FindTool("wt"))
                {
                    WorkingDirectory = folder,
                    UseShellExecute = true,
                };
                var args = invocation is null
                    ? new[] { "-d", folder }
                    : new[] { "-d", folder, shell, "-NoExit", "-Command", invocation };
                foreach (var arg in args) terminal.ArgumentList.Add(arg);
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
            foreach (var arg in invocation is null ? new[] { "-NoExit" } : new[] { "-NoExit", "-Command", invocation })
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
            var paths = SelectedPaths();
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
    private void PreviewToggle_Changed(object? sender, RoutedEventArgs e)
    {
        _showPreview = PreviewToggle.IsChecked == true;
        foreach (var tab in _tabs) tab.Host.ShowPreview = _showPreview;
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
