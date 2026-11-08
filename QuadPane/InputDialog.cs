using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using QuadPane.Shell;

namespace QuadPane;

/// <summary>Hộp thoại nhập một dòng chữ (đặt tên workspace...).</summary>
internal sealed class InputDialog : Window
{
    private readonly TextBox _input;
    private readonly Button _ok;
    private string? _result;

    private InputDialog(string title, string label, string initial, string okText)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new Avalonia.Media.FontFamily("Segoe UI Variable, Segoe UI");
        Bind(BackgroundProperty, this.GetResourceObservable("WindowFallbackBrush"));

        _input = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 0) };
        _ok = new Button { Content = okText, Classes = { "accent" }, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Huỷ", MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        _ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close();
        _input.TextChanged += (_, _) => _ok.IsEnabled = !string.IsNullOrWhiteSpace(_input.Text);
        _ok.IsEnabled = !string.IsNullOrWhiteSpace(initial);

        Content = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 20),
            Children =
            {
                new TextBlock { Text = label, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                _input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 20, 0, 0),
                    Children = { _ok, cancel },
                },
            },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter && _ok.IsEnabled) { Accept(); e.Handled = true; }
        };
        Opened += (_, _) =>
        {
            ShellTheme.ApplyTitleBar(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, ActualThemeVariant == ThemeVariant.Dark);
            _input.Focus();
            _input.SelectAll();
        };
    }

    private void Accept()
    {
        _result = _input.Text?.Trim();
        Close();
    }

    /// <summary>Trả về chuỗi đã nhập, hoặc null nếu người dùng huỷ.</summary>
    public static async Task<string?> ShowAsync(Window owner, string title, string label, string initial = "", string okText = "Lưu")
    {
        var dialog = new InputDialog(title, label, initial, okText);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }
}
