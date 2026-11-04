using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using QuadPane.Shell;

namespace QuadPane;

/// <summary>Một panel: thanh điều hướng + ô địa chỉ + Explorer Browser.</summary>
public partial class PaneView : UserControl
{
    public int Index { get; }

    /// <summary>Phát ra khi người dùng tương tác với panel (để MainWindow đánh dấu panel đang hoạt động).</summary>
    public event Action<PaneView>? Activated;

    /// <summary>Phát ra khi panel điều hướng sang thư mục mới.</summary>
    public event Action<PaneView>? PathChanged;

    public string? CurrentPath => Host.CurrentParsingName;
    public string DisplayPath => AddressBox.Text ?? "";
    public bool ShowTree => Host.ShowTree;

    // Constructor không tham số cho designer
    public PaneView() : this(0, null, false) { }

    public PaneView(int index, string? initialPath, bool showTree)
    {
        InitializeComponent();
        Index = index;
        Host.InitialPath = initialPath;
        Host.PropertyBagName = $"QuadPane.Pane{index}";
        Host.ShowTree = showTree;
        TreeToggle.IsChecked = showTree;
        Host.Navigated += (display, _) =>
        {
            AddressBox.Text = display;
            PathChanged?.Invoke(this);
        };
        // Tunnel: bắt cả khi click vào nút/ô địa chỉ bên trong
        AddHandler(PointerPressedEvent, (_, _) => Activated?.Invoke(this), RoutingStrategies.Tunnel);
    }

    public bool IsActive
    {
        set => Card.Classes.Set("active", value);
    }

    public bool Dark
    {
        set => Host.Dark = value;
    }

    public bool Owns(IntPtr hwnd) => Host.Owns(hwnd);
    internal bool TranslateAccelerator(ref MSG msg) => Host.TranslateAccelerator(ref msg);

    public void GoBack() => Host.GoBack();
    public void GoForward() => Host.GoForward();
    public void GoUp() => Host.GoUp();
    public void RefreshView() => Host.RefreshView();
    public void FocusView() => Host.FocusView();

    public void FocusAddress()
    {
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    private void Back_Click(object? sender, RoutedEventArgs e) => GoBack();
    private void Forward_Click(object? sender, RoutedEventArgs e) => GoForward();
    private void Up_Click(object? sender, RoutedEventArgs e) => GoUp();
    private void Refresh_Click(object? sender, RoutedEventArgs e) => RefreshView();

    private void TreeToggle_Changed(object? sender, RoutedEventArgs e)
    {
        Host.ShowTree = TreeToggle.IsChecked == true;
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
        else
        {
            AddressBox.Classes.Remove("error");
        }
    }
}
