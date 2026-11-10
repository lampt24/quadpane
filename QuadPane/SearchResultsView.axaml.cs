using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using QuadPane.Shell;

namespace QuadPane;

public partial class SearchResultsView : UserControl
{
    private enum SortColumn { Name, Modified, Directory, Size }

    private CancellationTokenSource? _searchCancellation;
    private IReadOnlyList<SearchResult> _results = [];
    private SortColumn? _sortColumn;
    private bool _sortAscending = true;
    public string Query { get; private set; } = "";
    public int ResultCount => ResultsList.ItemCount;
    public string? SelectedPath => (ResultsList.SelectedItem as SearchResult)?.FullPath;
    public event Action<string>? OpenFolder;
    public event Action<string, string?>? OpenLocation;
    public event Action? ResultsChanged;

    public SearchResultsView()
    {
        InitializeComponent();
        ResultsList.AddHandler(PointerPressedEvent, ResultsList_PointerPressed, RoutingStrategies.Tunnel);
        OpenLocationButton.IsEnabled = false;
        OpenFileButton.IsEnabled = false;
    }

    public async void Search(string query)
    {
        _searchCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        Query = query.Trim();
        _results = [];
        ResultsList.ItemsSource = null;
        ResultsChanged?.Invoke();
        if (Query.Length == 0)
        {
            ResultStatus.Text = "Nhập từ khóa để tìm kiếm.";
            _searchCancellation = null;
            return;
        }
        ResultStatus.Text = "Đang tìm kiếm...";
        try
        {
            await Task.Delay(250, cancellation.Token);
            var response = await Task.Run(() => EverythingSearch.Search(Query), cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            _results = response.Results;
            ApplySort();
            ResultsChanged?.Invoke();
            ResultStatus.Text = response.Total == 0 ? "Không có kết quả."
                : $"Hiển thị {response.Results.Count:N0} / {response.Total:N0} kết quả đầu tiên.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested)
                ResultStatus.Text = ex is DllNotFoundException ? "Thiếu Everything SDK DLL trong thư mục ứng dụng." : ex.Message;
        }
        finally
        {
            if (_searchCancellation == cancellation) _searchCancellation = null;
        }
    }

    public void Stop() => _searchCancellation?.Cancel();

    private void Header_Click(object? sender, RoutedEventArgs e)
    {
        var column = sender == NameHeader ? SortColumn.Name
            : sender == ModifiedHeader ? SortColumn.Modified
            : sender == DirectoryHeader ? SortColumn.Directory
            : SortColumn.Size;
        _sortAscending = _sortColumn != column || !_sortAscending;
        _sortColumn = column;
        NameHeader.Content = column == SortColumn.Name ? SortLabel("Name") : "Name";
        ModifiedHeader.Content = column == SortColumn.Modified ? SortLabel("Date modified") : "Date modified";
        DirectoryHeader.Content = column == SortColumn.Directory ? SortLabel("Directory") : "Directory";
        SizeHeader.Content = column == SortColumn.Size ? SortLabel("Size") : "Size";
        ApplySort();
    }

    private string SortLabel(string label) => $"{label} {(_sortAscending ? "▲" : "▼")}";

    private void ApplySort()
    {
        if (_sortColumn is null) { ResultsList.ItemsSource = _results; return; }
        var selected = ResultsList.SelectedItem;
        IOrderedEnumerable<SearchResult> sorted = _sortColumn switch
        {
            SortColumn.Name => _sortAscending
                ? _results.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                : _results.OrderByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase),
            SortColumn.Modified => _sortAscending
                ? _results.OrderBy(item => item.Modified)
                : _results.OrderByDescending(item => item.Modified),
            SortColumn.Directory => _sortAscending
                ? _results.OrderBy(item => item.Directory, StringComparer.OrdinalIgnoreCase)
                : _results.OrderByDescending(item => item.Directory, StringComparer.OrdinalIgnoreCase),
            _ => _sortAscending
                ? _results.OrderBy(item => item.Size)
                : _results.OrderByDescending(item => item.Size),
        };
        ResultsList.ItemsSource = sorted.ThenBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase).ToList();
        if (selected is SearchResult) ResultsList.SelectedItem = selected;
    }

    public void FocusResults()
    {
        if (ResultsList.ItemCount > 0 && ResultsList.SelectedIndex < 0) ResultsList.SelectedIndex = 0;
        ResultsList.Focus();
    }

    private void ResultsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = ResultsList.SelectedItem != null;
        OpenLocationButton.IsEnabled = hasSelection;
        OpenFileButton.IsEnabled = hasSelection;
    }

    private void ResultsList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) OpenContainingFolder();
            else OpenSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (ResultsList.SelectedItem is SearchResult result && TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            {
                _ = cb.SetTextAsync(result.FullPath);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            if (ResultsList.SelectedItem is SearchResult result) ShowItemContextMenu(result);
            e.Handled = true;
        }
    }

    private void ResultsList_DoubleTapped(object? sender, TappedEventArgs e) => OpenSelected();

    private void ResultsList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ResultsList).Properties.IsRightButtonPressed || e.Source is not Visual visual) return;
        var item = visual.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (item?.DataContext is not SearchResult result) return;
        ResultsList.SelectedItem = result;
        e.Handled = true;
        ShowItemContextMenu(result);
    }

    private void ShowItemContextMenu(SearchResult result)
    {
        var menu = new ContextMenu();
        var openItem = new MenuItem { Header = "Mở (Enter)" };
        openItem.Click += (_, _) => OpenSelected();
        menu.Items.Add(openItem);

        var openFolderItem = new MenuItem { Header = "Mở vị trí tệp trong QuadPane (Ctrl+Enter)" };
        openFolderItem.Click += (_, _) => OpenContainingFolder();
        menu.Items.Add(openFolderItem);

        var revealItem = new MenuItem { Header = "Mở vị trí trong Windows Explorer" };
        revealItem.Click += (_, _) => RevealInExplorer(result.FullPath);
        menu.Items.Add(revealItem);

        menu.Items.Add(new Separator());

        var copyPathItem = new MenuItem { Header = "Sao chép đường dẫn (Ctrl+C)" };
        copyPathItem.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                await cb.SetTextAsync(result.FullPath);
        };
        menu.Items.Add(copyPathItem);

        menu.Items.Add(new Separator());

        var windowsMenuItem = new MenuItem { Header = "Menu Windows..." };
        windowsMenuItem.Click += (_, _) =>
        {
            try
            {
                var owner = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                ShellContextMenu.Show(owner, result.FullPath);
            }
            catch (Exception ex) { ResultStatus.Text = $"Không thể mở menu Windows: {ex.Message}"; }
        };
        menu.Items.Add(windowsMenuItem);

        menu.Open(ResultsList);
    }

    private void OpenFile_Click(object? sender, RoutedEventArgs e) => OpenSelected();

    private void OpenLocation_Click(object? sender, RoutedEventArgs e) => OpenContainingFolder();

    private void OpenContainingFolder()
    {
        if (ResultsList.SelectedItem is not SearchResult result) return;
        try
        {
            string targetFolder = !string.IsNullOrEmpty(result.Directory) ? result.Directory : result.FullPath;
            string? itemName = !result.IsFolder ? result.Name : Path.GetFileName(result.FullPath.TrimEnd('\\', '/'));
            if (Directory.Exists(targetFolder))
            {
                if (OpenLocation is not null) OpenLocation.Invoke(targetFolder, itemName);
                else OpenFolder?.Invoke(targetFolder);
            }
            else if (File.Exists(result.FullPath))
            {
                var dir = Path.GetDirectoryName(result.FullPath);
                var name = Path.GetFileName(result.FullPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    if (OpenLocation is not null) OpenLocation.Invoke(dir, name);
                    else OpenFolder?.Invoke(dir);
                }
            }
        }
        catch (Exception ex)
        {
            ResultStatus.Text = $"Không thể mở vị trí: {ex.Message}";
        }
    }

    private void RevealInExplorer(string fullPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ResultStatus.Text = $"Không thể mở Explorer: {ex.Message}";
        }
    }

    private void OpenSelected()
    {
        if (ResultsList.SelectedItem is not SearchResult result) return;
        try
        {
            if (result.IsFolder) OpenFolder?.Invoke(result.FullPath);
            else
            {
                using var process = Process.Start(new ProcessStartInfo(result.FullPath) { UseShellExecute = true })
                    ?? throw new InvalidOperationException("Không thể mở tệp.");
            }
        }
        catch (Exception ex) { ResultStatus.Text = $"Không thể mở kết quả: {ex.Message}"; }
    }
}
