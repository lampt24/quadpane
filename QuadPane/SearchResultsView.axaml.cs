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
    private CancellationTokenSource? _searchCancellation;
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
            ResultsList.ItemsSource = response.Results;
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
