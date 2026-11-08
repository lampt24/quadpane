using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace QuadPane;

/// <summary>
/// Bảng lệnh (Ctrl+Shift+P) kiểu VS Code: gõ để tìm lệnh, workspace, thư mục yêu thích / gần đây.
/// Tìm không dấu: "mo lai tab" khớp "Mở lại tab vừa đóng".
/// </summary>
internal sealed class CommandPalette : Window
{
    public sealed record Entry(string Title, string Group, string Hint, Action Run)
    {
        public string Search { get; } = CommandCatalog.Fold($"{Title} {Group}");
    }

    private readonly IReadOnlyList<Entry> _entries;
    private readonly TextBox _input;
    private readonly ListBox _list;
    private readonly TextBlock _empty;
    private bool _closing;

    public CommandPalette(IReadOnlyList<Entry> entries)
    {
        _entries = entries;
        Width = 600;
        Height = 440;
        CanResize = false;
        ShowInTaskbar = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 0;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");
        Bind(BackgroundProperty, this.GetResourceObservable("PaneBackgroundBrush"));

        _input = new TextBox
        {
            PlaceholderText = "Gõ tên lệnh, workspace hoặc thư mục...",
            Margin = new Thickness(10, 10, 10, 6),
            FontSize = 14,
            Padding = new Thickness(10, 8),
        };
        _list = new ListBox
        {
            Background = Brushes.Transparent,
            Margin = new Thickness(6, 0, 6, 6),
            ItemTemplate = new FuncDataTemplate<Entry?>((entry, _) => entry is null ? new Panel() : BuildRow(entry)),
        };
        _empty = new TextBlock
        {
            Text = "Không có lệnh phù hợp.",
            Margin = new Thickness(20, 12),
            IsVisible = false,
        };
        _empty.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedTextBrush"));

        var root = new DockPanel();
        DockPanel.SetDock(_input, Dock.Top);
        DockPanel.SetDock(_empty, Dock.Top);
        root.Children.Add(_input);
        root.Children.Add(_empty);
        root.Children.Add(_list);
        var frame = new Border { BorderThickness = new Thickness(1), Child = root };
        frame.Bind(Border.BorderBrushProperty, this.GetResourceObservable("PaneBorderBrush"));
        Content = frame;

        _input.TextChanged += (_, _) => UpdateList();
        _input.AddHandler(KeyDownEvent, Input_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _list.DoubleTapped += (_, _) => RunSelected();
        _list.KeyDown += (_, e) => { if (e.Key == Key.Enter) { RunSelected(); e.Handled = true; } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Deactivated += (_, _) => { if (!_closing) Close(); };
        Closing += (_, _) => _closing = true;
        Opened += (_, _) => _input.Focus();
        UpdateList();
    }

    private Control BuildRow(Entry entry)
    {
        var title = new TextBlock { Text = entry.Title, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var group = new TextBlock { Text = entry.Group, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        group.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedTextBrush"));
        var hint = new TextBlock { Text = entry.Hint, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontFamily = new FontFamily("Cascadia Mono, Consolas") };
        hint.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("MutedTextBrush"));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(4, 2) };
        Grid.SetColumn(group, 1);
        Grid.SetColumn(hint, 2);
        grid.Children.Add(title);
        grid.Children.Add(group);
        grid.Children.Add(hint);
        return grid;
    }

    private void UpdateList()
    {
        var tokens = CommandCatalog.Fold(_input.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = _entries.Where(e => tokens.All(t => e.Search.Contains(t))).ToList();
        _list.ItemsSource = matches;
        _list.SelectedIndex = matches.Count > 0 ? 0 : -1;
        _empty.IsVisible = matches.Count == 0;
    }

    private void Input_KeyDown(object? sender, KeyEventArgs e)
    {
        int count = _list.ItemCount;
        switch (e.Key)
        {
            case Key.Down when count > 0:
                _list.SelectedIndex = (_list.SelectedIndex + 1) % count;
                _list.ScrollIntoView(_list.SelectedIndex);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                _list.SelectedIndex = (_list.SelectedIndex - 1 + count) % count;
                _list.ScrollIntoView(_list.SelectedIndex);
                e.Handled = true;
                break;
            case Key.Enter:
                RunSelected();
                e.Handled = true;
                break;
        }
    }

    private void RunSelected()
    {
        if (_list.SelectedItem is not Entry entry) return;
        _closing = true;
        Close();
        // Chạy sau khi bảng đóng để focus trả về cửa sổ chính trước
        Avalonia.Threading.Dispatcher.UIThread.Post(entry.Run);
    }

    /// <summary>Hiện ở giữa phía trên cửa sổ chính, giống VS Code.</summary>
    public void ShowNear(Window owner)
    {
        var scale = owner.RenderScaling;
        var x = owner.Position.X + (int)((owner.Bounds.Width - Width) / 2 * scale);
        var y = owner.Position.Y + (int)(56 * scale);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(Math.Max(x, owner.Position.X), y);
        Show(owner);
    }
}
