using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace QuadPane;

public partial class SearchWindow : Window
{
    private readonly Func<string, bool> _navigateFolder;
    private CancellationTokenSource? _searchCancellation;

    // Constructor không tham số cho designer
    public SearchWindow() : this(_ => false) { }

    public SearchWindow(Func<string, bool> navigateFolder)
    {
        InitializeComponent();
        _navigateFolder = navigateFolder;
        Opened += (_, _) => QueryBox.Focus();
        Closed += (_, _) => _searchCancellation?.Cancel();
    }

    public void FocusSearch()
    {
        Activate();
        QueryBox.Focus();
        QueryBox.SelectAll();
    }

    private async void QueryBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        string query = QueryBox.Text?.Trim() ?? "";
        ResultsList.ItemsSource = null;
        if (query.Length == 0)
        {
            ResultStatus.Text = "Nhập từ khóa để tìm kiếm.";
            return;
        }

        ResultStatus.Text = "Đang tìm kiếm...";
        try
        {
            await Task.Delay(250, cancellation.Token);
            var response = await Task.Run(() => EverythingSearch.Search(query), cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            ResultsList.ItemsSource = response.Results;
            ResultStatus.Text = response.Total == 0 ? "Không có kết quả."
                : $"Hiển thị {response.Results.Count:N0} / {response.Total:N0} kết quả đầu tiên.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or InvalidOperationException or PlatformNotSupportedException)
        {
            if (!cancellation.IsCancellationRequested)
                ResultStatus.Text = ex is DllNotFoundException ? "Thiếu Everything SDK DLL trong thư mục ứng dụng." : ex.Message;
        }
    }

    private void QueryBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (ResultsList.ItemCount > 0)
            {
                ResultsList.SelectedIndex = 0;
                ResultsList.Focus();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (ResultsList.ItemCount > 0)
            {
                ResultsList.SelectedIndex = 0;
                OpenSelected();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) Close();
    }

    private void ResultsList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
        else if (e.Key == Key.Escape) { QueryBox.Focus(); e.Handled = true; }
    }

    private void ResultsList_DoubleTapped(object? sender, TappedEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (ResultsList.SelectedItem is not SearchResult result) return;
        try
        {
            if (result.IsFolder)
            {
                if (!_navigateFolder(result.FullPath))
                    throw new IOException("Không thể mở thư mục trong panel hiện tại.");
            }
            else
            {
                Process.Start(new ProcessStartInfo(result.FullPath) { UseShellExecute = true });
            }
            Close();
        }
        catch (Exception ex)
        {
            ResultStatus.Text = $"Không thể mở kết quả: {ex.Message}";
        }
    }
}
