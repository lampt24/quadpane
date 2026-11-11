using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuadPane.Shell;

namespace QuadPane;

public partial class TreeSizeWindow : Window
{
    private sealed record DriveOption(string Title, string? Path, bool IsAllDrives)
    {
        public override string ToString() => Title;
    }

    private enum SortField { Name, Size, Modified }

    private readonly MainWindow? _main;
    private readonly ObservableCollection<TreeSizeItem> _rootItems = new();
    private readonly List<DriveOption> _driveOptions = new();
    private SortField _sortField = SortField.Size;
    private bool _sortAscending = false;
    private string _filterText = "";
    private bool _isPopulatingSelector;
    private bool _isDeleting;

    public ObservableCollection<TreeSizeItem> RootItems => _rootItems;

    public TreeSizeWindow() : this(null, null) { }

    internal TreeSizeWindow(MainWindow? main, string? initialPath = null)
    {
        InitializeComponent();
        _main = main;

        TreeList.ItemsSource = _rootItems;
        TreeList.AddHandler(TreeViewItem.ExpandedEvent, TreeList_Expanded);
        TreeList.AddHandler(PointerPressedEvent, TreeList_PointerPressed, RoutingStrategies.Tunnel);
        TreeList.SelectionChanged += TreeList_SelectionChanged;

        CheckEverythingIndexStatus();
        PopulateDriveSelector(initialPath);

        TitleBar.PointerPressed += TitleBar_PointerPressed;
        ActualThemeVariantChanged += (_, _) => ApplyThemeToNative();
        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeToNative();
            HideDrawnTitle();
        }, DispatcherPriority.Background);
    }

    private void ApplyThemeToNative()
    {
        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        ShellTheme.ApplyTitleBar(hwnd, ActualThemeVariant == ThemeVariant.Dark);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            BeginMoveDrag(e);
    }

    private void HideDrawnTitle()
    {
        if (this.GetVisualParent() is not Visual host) return;
        foreach (var v in host.GetVisualDescendants())
        {
            if (v is Control { Name: "PART_TitleTextPanel" or "PART_FullScreenButton" } c)
            {
                c.IsVisible = false;
                c.IsHitTestVisible = false;
            }
        }
    }

    private void CheckEverythingIndexStatus()
    {
        bool indexed = EverythingSearch.IsFolderSizeIndexed();
        EverythingBanner.IsVisible = !indexed;
    }

    private void EnableEverythingFolderSize_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (EverythingSearch.TryEnableFolderSizeIndexing())
            {
                EverythingBanner.IsVisible = false;
                UpdateStatus("Đã kích hoạt Index Folder Size trong Everything. Đang làm mới...");
                RefreshCurrentSource();
            }
            else
            {
                UpdateStatus("Không thể bật tự động trong Everything. Hãy mở Options > Indexes trong Everything và tích 'Index folder size'.");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"Lỗi: {ex.Message}");
        }
    }

    private void PopulateDriveSelector(string? initialPath)
    {
        _isPopulatingSelector = true;
        _driveOptions.Clear();

        _driveOptions.Add(new DriveOption("Toàn bộ máy tính (This PC)", null, true));

        DriveOption? selectedOption = null;
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? (drive.DriveType == DriveType.Fixed ? "Local Disk" : "Ổ đĩa")
                    : drive.VolumeLabel;
                long total = drive.TotalSize;
                long free = drive.TotalFreeSpace;
                long used = Math.Max(0, total - free);
                string title = $"Ổ {drive.Name.TrimEnd('\\')} ({label}) - {SearchResult.FormatBytes(used)} / {SearchResult.FormatBytes(total)}";
                var opt = new DriveOption(title, drive.RootDirectory.FullName, false);
                _driveOptions.Add(opt);

                if (!string.IsNullOrEmpty(initialPath) &&
                    string.Equals(drive.RootDirectory.FullName.TrimEnd('\\'), initialPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    selectedOption = opt;
                }
            }
        }

        if (selectedOption is null && !string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            var customOpt = new DriveOption($"📁 {initialPath}", initialPath, false);
            _driveOptions.Add(customOpt);
            selectedOption = customOpt;
        }

        DriveSelector.ItemsSource = _driveOptions;
        DriveSelector.SelectedItem = selectedOption ?? _driveOptions[0];
        _isPopulatingSelector = false;

        LoadSource((DriveOption)DriveSelector.SelectedItem);
    }

    private void DriveSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingSelector) return;
        if (DriveSelector.SelectedItem is DriveOption opt)
        {
            LoadSource(opt);
        }
    }

    private void LoadSource(DriveOption option)
    {
        _rootItems.Clear();
        UpdateStatus("Đang tải danh sách ổ đĩa và thư mục...");

        if (option.IsAllDrives)
        {
            long totalCapacity = 0;
            long totalUsed = 0;
            if (OperatingSystem.IsWindows())
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady) continue;
                    long total = drive.TotalSize;
                    long free = drive.TotalFreeSpace;
                    long used = Math.Max(0, total - free);
                    totalCapacity += total;
                    totalUsed += used;

                    string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? (drive.DriveType == DriveType.Fixed ? "Local Disk" : "Ổ đĩa")
                        : drive.VolumeLabel;
                    string name = $"{label} ({drive.Name.TrimEnd('\\')})";

                    var item = new TreeSizeItem
                    {
                        Name = name,
                        FullPath = drive.RootDirectory.FullName,
                        IsDrive = true,
                        IsFolder = true,
                        Size = used,
                        ParentSize = total,
                        Modified = null,
                    };
                    item.Children.Add(new TreeSizeItem { FullPath = "" });
                    _rootItems.Add(item);
                }
            }

            SortItems(_rootItems);
            UpdateStatus($"This PC: {_rootItems.Count} ổ đĩa | Đã dùng: {SearchResult.FormatBytes(totalUsed)} / {SearchResult.FormatBytes(totalCapacity)}");

            if (_rootItems.Count > 0)
            {
                _rootItems[0].IsExpanded = true;
                _ = LoadChildrenAsync(_rootItems[0]);
            }
        }
        else if (!string.IsNullOrEmpty(option.Path) && Directory.Exists(option.Path))
        {
            var dir = new DirectoryInfo(option.Path);
            long folderSize = 0;
            try
            {
                folderSize = EverythingSearch.GetFolderSize(dir.FullName) ?? 0;
            }
            catch { }

            var root = new TreeSizeItem
            {
                Name = string.IsNullOrEmpty(dir.Name) ? dir.FullName : dir.Name,
                FullPath = dir.FullName,
                IsDrive = dir.Parent is null,
                IsFolder = true,
                Size = folderSize,
                ParentSize = folderSize > 0 ? folderSize : 1,
                Modified = dir.LastWriteTime,
            };
            root.Children.Add(new TreeSizeItem { FullPath = "" });
            _rootItems.Add(root);

            root.IsExpanded = true;
            _ = LoadChildrenAsync(root);
            UpdateStatus($"Phân tích thư mục: {root.FullPath} ({root.SizeText})");
        }
    }

    private async void TreeList_Expanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem tvi && tvi.DataContext is TreeSizeItem item)
        {
            await LoadChildrenAsync(item);
        }
    }

    private async Task LoadChildrenAsync(TreeSizeItem item)
    {
        if (item.IsLoading || (item.IsLoaded && !item.HasDummyChild)) return;

        item.IsLoading = true;
        item.Children.Clear();

        string path = item.FullPath;
        long parentSize = item.Size;

        var children = await Task.Run(() => FetchChildren(path, parentSize));

        foreach (var child in children)
        {
            item.Children.Add(child);
        }

        if (item.Size == 0 && children.Count > 0)
        {
            long sum = children.Sum(c => c.Size);
            item.Size = sum;
            foreach (var child in children) child.ParentSize = item.Size;
        }

        item.FileCount = children.Count(c => !c.IsFolder);
        item.FolderCount = children.Count(c => c.IsFolder);
        item.IsLoaded = true;
        item.IsLoading = false;

        UpdateStatus($"Đã nạp {item.Name}: {children.Count:N0} mục ({item.FolderCount:N0} thư mục, {item.FileCount:N0} tệp) - {item.SizeText}");
    }

    private List<TreeSizeItem> FetchChildren(string folderPath, long parentSize)
    {
        var items = new List<TreeSizeItem>();
        bool usedEverything = false;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var sdkResults = EverythingSearch.GetDirectChildren(folderPath);
                if (sdkResults.Count > 0)
                {
                    usedEverything = true;
                    foreach (var r in sdkResults)
                    {
                        var child = new TreeSizeItem
                        {
                            Name = r.Name,
                            FullPath = r.FullPath,
                            IsDrive = false,
                            IsFolder = r.IsFolder,
                            Size = r.Size ?? 0,
                            ParentSize = parentSize,
                            Modified = r.Modified,
                        };
                        if (r.IsFolder)
                        {
                            child.Children.Add(new TreeSizeItem { FullPath = "" });
                        }
                        items.Add(child);
                    }
                }
            }
            catch { }
        }

        if (!usedEverything)
        {
            try
            {
                var dirInfo = new DirectoryInfo(folderPath);
                if (dirInfo.Exists)
                {
                    foreach (var di in dirInfo.EnumerateDirectories())
                    {
                        long size = 0;
                        try { size = EverythingSearch.GetFolderSize(di.FullName) ?? 0; }
                        catch { }

                        DateTime? modified = null;
                        try { modified = di.LastWriteTime; } catch { }

                        var child = new TreeSizeItem
                        {
                            Name = di.Name,
                            FullPath = di.FullName,
                            IsDrive = false,
                            IsFolder = true,
                            Size = size,
                            ParentSize = parentSize,
                            Modified = modified,
                        };
                        child.Children.Add(new TreeSizeItem { FullPath = "" });
                        items.Add(child);
                    }

                    foreach (var fi in dirInfo.EnumerateFiles())
                    {
                        DateTime? modified = null;
                        try { modified = fi.LastWriteTime; } catch { }

                        items.Add(new TreeSizeItem
                        {
                            Name = fi.Name,
                            FullPath = fi.FullName,
                            IsDrive = false,
                            IsFolder = false,
                            Size = fi.Length,
                            ParentSize = parentSize,
                            Modified = modified,
                        });
                    }
                }
            }
            catch { }
        }

        SortItems(items);
        return items;
    }

    private void SortItems(IList<TreeSizeItem> list)
    {
        var sorted = _sortField switch
        {
            SortField.Name => _sortAscending
                ? list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList()
                : list.OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            SortField.Modified => _sortAscending
                ? list.OrderBy(x => x.Modified).ToList()
                : list.OrderByDescending(x => x.Modified).ToList(),
            _ => _sortAscending
                ? list.OrderBy(x => x.Size).ToList()
                : list.OrderByDescending(x => x.Size).ToList(),
        };

        if (list is ObservableCollection<TreeSizeItem> obs)
        {
            for (int i = 0; i < sorted.Count; i++)
            {
                int oldIndex = obs.IndexOf(sorted[i]);
                if (oldIndex >= 0 && oldIndex != i)
                {
                    obs.Move(oldIndex, i);
                }
            }
        }
        else if (list is List<TreeSizeItem> l)
        {
            l.Clear();
            l.AddRange(sorted);
        }
    }

    private void SortTreeRecursive(TreeSizeItem node)
    {
        if (node.Children.Count > 1 && !node.HasDummyChild)
        {
            SortItems(node.Children);
            foreach (var child in node.Children)
            {
                SortTreeRecursive(child);
            }
        }
    }

    private void ApplySort(SortField field)
    {
        if (_sortField == field)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortField = field;
            _sortAscending = field == SortField.Name;
        }

        SortItems(_rootItems);
        foreach (var root in _rootItems)
        {
            SortTreeRecursive(root);
        }
    }

    private void SortByName_Click(object? sender, RoutedEventArgs e) => ApplySort(SortField.Name);
    private void SortBySize_Click(object? sender, RoutedEventArgs e) => ApplySort(SortField.Size);
    private void SortByModified_Click(object? sender, RoutedEventArgs e) => ApplySort(SortField.Modified);

    private void TreeList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = TreeList.SelectedItem is TreeSizeItem;
        OpenInQuadPaneButton.IsEnabled = hasSelection;
        OpenInExplorerButton.IsEnabled = hasSelection;
        DeleteFolderButton.IsEnabled = !_isDeleting && TreeList.SelectedItem is TreeSizeItem { CanDeleteFolder: true };

        if (TreeList.SelectedItem is TreeSizeItem item)
        {
            UpdateStatus($"{item.FullPath} | Dung lượng: {item.SizeText} ({item.PercentText}) | Sửa đổi: {item.ModifiedText}");
        }
    }

    private void TreeList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(TreeList).Properties.IsRightButtonPressed || e.Source is not Visual visual) return;
        var tvi = visual.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        if (tvi?.DataContext is not TreeSizeItem item) return;
        TreeList.SelectedItem = item;
        e.Handled = true;
        ShowItemContextMenu(item);
    }

    private void TreeList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (TreeList.SelectedItem is TreeSizeItem item)
            {
                if (item.IsFolder) OpenInQuadPane(item);
                else OpenFile(item.FullPath);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (TreeList.SelectedItem is TreeSizeItem item && TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            {
                _ = cb.SetTextAsync(item.FullPath);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete)
        {
            if (TreeList.SelectedItem is TreeSizeItem item) _ = DeleteFolderAsync(item);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            RefreshCurrentSource();
            e.Handled = true;
        }
        else if (e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            if (TreeList.SelectedItem is TreeSizeItem item)
            {
                ShowItemContextMenu(item);
                e.Handled = true;
            }
        }
    }

    private void TreeList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (TreeList.SelectedItem is not TreeSizeItem item) return;
        if (!item.IsFolder)
        {
            OpenFile(item.FullPath);
        }
        else
        {
            item.IsExpanded = !item.IsExpanded;
        }
    }

    private void ShowItemContextMenu(TreeSizeItem item)
    {
        var menu = new ContextMenu();

        var openQuadPane = new MenuItem { Header = "Mở trong QuadPane (Enter)" };
        openQuadPane.Click += (_, _) => OpenInQuadPane(item);
        menu.Items.Add(openQuadPane);

        var openExplorer = new MenuItem { Header = "Mở vị trí trong Windows Explorer" };
        openExplorer.Click += (_, _) => RevealInExplorer(item.FullPath);
        menu.Items.Add(openExplorer);

        var openTerminal = new MenuItem { Header = "Mở Terminal tại đây" };
        openTerminal.Click += (_, _) => OpenTerminal(item.FullPath);
        menu.Items.Add(openTerminal);

        menu.Items.Add(new Separator());

        var copyPath = new MenuItem { Header = "Sao chép đường dẫn (Ctrl+C)" };
        copyPath.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                await cb.SetTextAsync(item.FullPath);
        };
        menu.Items.Add(copyPath);

        var reload = new MenuItem { Header = "Quét lại mục này" };
        reload.Click += async (_, _) => await ReloadItemAsync(item);
        menu.Items.Add(reload);

        menu.Items.Add(new Separator());

        var deleteFolder = new MenuItem { Header = "Xóa thư mục vào Thùng rác (Del)", IsEnabled = item.CanDeleteFolder && !_isDeleting };
        deleteFolder.Click += async (_, _) => await DeleteFolderAsync(item);
        menu.Items.Add(deleteFolder);
        menu.Items.Add(new Separator());

        var windowsMenu = new MenuItem { Header = "Menu Windows..." };
        windowsMenu.Click += (_, _) =>
        {
            try
            {
                var owner = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                ShellContextMenu.Show(owner, item.FullPath);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Không thể mở menu Windows: {ex.Message}");
            }
        };
        menu.Items.Add(windowsMenu);

        menu.Open(TreeList);
    }

    private async void DeleteFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (TreeList.SelectedItem is TreeSizeItem item) await DeleteFolderAsync(item);
    }

    private async Task DeleteFolderAsync(TreeSizeItem item)
    {
        if (_isDeleting || !item.CanDeleteFolder || !OperatingSystem.IsWindows()) return;
        if (!UpdateService.Confirm(this,
            $"Chuyển thư mục và toàn bộ nội dung vào Thùng rác?\n\n{item.FullPath}", "Xóa thư mục")) return;

        _isDeleting = true;
        DeleteFolderButton.IsEnabled = false;
        UpdateStatus($"Đang xóa: {item.FullPath}");
        try
        {
            await Task.Run(() => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(item.FullPath,
                Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException));
            if (!_rootItems.Remove(item))
                foreach (var root in _rootItems)
                    if (root.RemoveDescendant(item))
                    {
                        if (!root.IsDrive) root.ParentSize = root.Size;
                        break;
                    }
            TreeList.SelectedItem = null;
            UpdateStatus($"Đã chuyển vào Thùng rác: {item.FullPath}");
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Đã hủy xóa thư mục. Nhấn F5 để cập nhật dung lượng.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Không thể xóa: {item.FullPath}. Nhấn F5 để cập nhật dung lượng.");
            UpdateService.ShowError(this, ex.Message, "Không thể xóa thư mục");
        }
        finally
        {
            _isDeleting = false;
            DeleteFolderButton.IsEnabled = TreeList.SelectedItem is TreeSizeItem { CanDeleteFolder: true };
        }
    }

    private async Task ReloadItemAsync(TreeSizeItem item)
    {
        item.Children.Clear();
        item.IsLoaded = false;
        item.Children.Add(new TreeSizeItem { FullPath = "" });
        item.IsExpanded = true;
        await LoadChildrenAsync(item);
    }

    private void OpenInQuadPane_Click(object? sender, RoutedEventArgs e)
    {
        if (TreeList.SelectedItem is TreeSizeItem item)
        {
            OpenInQuadPane(item);
        }
    }

    private void OpenInExplorer_Click(object? sender, RoutedEventArgs e)
    {
        if (TreeList.SelectedItem is TreeSizeItem item)
        {
            RevealInExplorer(item.FullPath);
        }
    }

    private void OpenInQuadPane(TreeSizeItem item)
    {
        string targetFolder = item.IsFolder ? item.FullPath : (Path.GetDirectoryName(item.FullPath) ?? item.FullPath);
        string? selectItem = item.IsFolder ? null : item.Name;
        if (_main is not null)
        {
            _main.OpenLocation(targetFolder, selectItem);
            _main.Activate();
        }
        else
        {
            RevealInExplorer(item.FullPath);
        }
    }

    private static void OpenFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch { }
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path))
                Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch { }
    }

    private static void OpenTerminal(string path)
    {
        try
        {
            string dir = Directory.Exists(path) ? path : (Path.GetDirectoryName(path) ?? path);
            Process.Start(new ProcessStartInfo("cmd.exe") { WorkingDirectory = dir, UseShellExecute = true });
        }
        catch { }
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Chọn thư mục cần phân tích dung lượng",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            var customOpt = new DriveOption($"📁 {path}", path, false);
            _driveOptions.Add(customOpt);
            DriveSelector.SelectedItem = customOpt;
        }
    }

    private void RefreshButton_Click(object? sender, RoutedEventArgs e) => RefreshCurrentSource();

    private void RefreshCurrentSource()
    {
        CheckEverythingIndexStatus();
        if (DriveSelector.SelectedItem is DriveOption opt)
        {
            LoadSource(opt);
        }
    }

    private void ExpandOneLevelButton_Click(object? sender, RoutedEventArgs e)
    {
        if (TreeList.SelectedItem is TreeSizeItem selected)
        {
            ExpandItemAndChildren(selected);
        }
        else
        {
            foreach (var root in _rootItems)
            {
                ExpandItemAndChildren(root);
            }
        }
    }

    private void ExpandItemAndChildren(TreeSizeItem item)
    {
        item.IsExpanded = true;
        if (!item.IsLoaded) _ = LoadChildrenAsync(item);
        foreach (var child in item.Children)
        {
            if (child.IsFolder)
            {
                child.IsExpanded = true;
                if (!child.IsLoaded) _ = LoadChildrenAsync(child);
            }
        }
    }

    private void CollapseAllButton_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var root in _rootItems)
        {
            CollapseRecursive(root);
        }
    }

    private static void CollapseRecursive(TreeSizeItem item)
    {
        item.IsExpanded = false;
        foreach (var child in item.Children)
        {
            CollapseRecursive(child);
        }
    }

    private void FilterBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _filterText = FilterBox.Text?.Trim() ?? "";
        // If filter is empty, nothing to hide
        // Future enhancement: filter view
    }

    private void UpdateStatus(string message) => StatusLabel.Text = message;
}
