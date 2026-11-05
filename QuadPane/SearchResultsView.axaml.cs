using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    public event Action? ResultsChanged;

    public SearchResultsView()
    {
        InitializeComponent();
        ResultsList.AddHandler(PointerPressedEvent, ResultsList_PointerPressed, RoutingStrategies.Tunnel);
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

    private void ResultsList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
        else if (e.Key == Key.Apps || e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (ResultsList.SelectedItem is SearchResult result) ShowContextMenu(result);
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
        ShowContextMenu(result);
    }

    private void ShowContextMenu(SearchResult result)
    {
        try
        {
            var owner = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            ShellContextMenu.Show(owner, result.FullPath);
        }
        catch (Exception ex) { ResultStatus.Text = $"Không thể mở menu Windows: {ex.Message}"; }
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
