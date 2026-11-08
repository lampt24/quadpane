using System.Globalization;
using System.Text;
using Avalonia.Input;

namespace QuadPane;

/// <summary>Các lệnh của app: dùng chung cho phím tắt, command palette và trang Phím tắt trong Cài đặt.</summary>
public enum CommandId
{
    Pane1, Pane2, Pane3, Pane4,
    NewTab, CloseTab, ReopenTab, DuplicateTab, NextTab, PreviousTab,
    Address, Search, Filter, Back, Forward, Up, Refresh,
    Favorites, AddFavorite,
    CopyToOther, MoveToOther,
    OpenTerminal, ToggleTree, TogglePreview,
    SaveWorkspace,
    LayoutQuad, LayoutTriple, LayoutDual, LayoutSingle, Equalize, ToggleTheme,
    CommandPalette, Settings,
}

public sealed record CommandInfo(CommandId Id, string Group, string Title, params string[] DefaultGestures);

/// <summary>Tổ hợp phím: một phím chính và các phím bổ trợ Ctrl / Alt / Shift.</summary>
public readonly record struct KeyChord(Key Key, KeyModifiers Modifiers)
{
    private const KeyModifiers Supported = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift;

    public static KeyChord Create(Key key, KeyModifiers modifiers) => new(key, modifiers & Supported);

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None;

    /// <summary>Dạng lưu trong layout.json, ví dụ "Ctrl+Shift+T", "Alt+Left", "Ctrl+OemComma".</summary>
    public string Serialize()
    {
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(KeyModifiers.Control)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) sb.Append("Shift+");
        return sb.Append(Key).ToString();
    }

    public static KeyChord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= KeyModifiers.Control; break;
                case "alt": modifiers |= KeyModifiers.Alt; break;
                case "shift": modifiers |= KeyModifiers.Shift; break;
                default: return null;
            }
        }
        return Enum.TryParse<Key>(parts[^1], ignoreCase: true, out var key) && !IsModifierKey(key)
            ? new KeyChord(key, modifiers) : null;
    }

    /// <summary>Dạng hiển thị, ví dụ "Ctrl+,", "Alt+←".</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(KeyModifiers.Control)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) sb.Append("Shift+");
        return sb.Append(KeyName(Key)).ToString();
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {(int)(key - Key.NumPad0)}",
        Key.Left => "←", Key.Right => "→", Key.Up => "↑", Key.Down => "↓",
        Key.OemComma => ",", Key.OemPeriod => ".", Key.OemMinus => "-", Key.OemPlus => "=",
        Key.OemQuestion => "/", Key.OemSemicolon => ";", Key.OemQuotes => "'", Key.OemTilde => "`",
        Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]", Key.OemPipe => "\\",
        Key.Return => "Enter", Key.Back => "Backspace", Key.Escape => "Esc",
        Key.Prior => "PageUp", Key.Next => "PageDown", Key.Delete => "Del", Key.Insert => "Ins",
        _ => key.ToString(),
    };

    /// <summary>
    /// Đổi virtual-key của Win32 sang <see cref="Key"/> của Avalonia (cho phím đi qua hook của Explorer nhúng).
    /// Enum Key của Avalonia theo thứ tự của WPF nên các dải liên tục có thể cộng offset.
    /// </summary>
    public static Key FromVirtualKey(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A => Key.A + (vk - 0x41),
        >= 0x30 and <= 0x39 => Key.D0 + (vk - 0x30),
        >= 0x60 and <= 0x69 => Key.NumPad0 + (vk - 0x60),
        >= 0x70 and <= 0x87 => Key.F1 + (vk - 0x70),
        0x08 => Key.Back, 0x09 => Key.Tab, 0x0D => Key.Return, 0x1B => Key.Escape, 0x20 => Key.Space,
        0x21 => Key.PageUp, 0x22 => Key.PageDown, 0x23 => Key.End, 0x24 => Key.Home,
        0x25 => Key.Left, 0x26 => Key.Up, 0x27 => Key.Right, 0x28 => Key.Down,
        0x2D => Key.Insert, 0x2E => Key.Delete,
        0x6A => Key.Multiply, 0x6B => Key.Add, 0x6D => Key.Subtract, 0x6E => Key.Decimal, 0x6F => Key.Divide,
        0xBA => Key.OemSemicolon, 0xBB => Key.OemPlus, 0xBC => Key.OemComma, 0xBD => Key.OemMinus,
        0xBE => Key.OemPeriod, 0xBF => Key.OemQuestion, 0xC0 => Key.OemTilde, 0xDB => Key.OemOpenBrackets,
        0xDC => Key.OemPipe, 0xDD => Key.OemCloseBrackets, 0xDE => Key.OemQuotes,
        _ => Key.None,
    };
}

public static class CommandCatalog
{
    public static readonly IReadOnlyList<CommandInfo> All =
    [
        new(CommandId.Pane1, "Panel", "Chọn panel 1", "Ctrl+D1"),
        new(CommandId.Pane2, "Panel", "Chọn panel 2", "Ctrl+D2"),
        new(CommandId.Pane3, "Panel", "Chọn panel 3", "Ctrl+D3"),
        new(CommandId.Pane4, "Panel", "Chọn panel 4", "Ctrl+D4"),
        new(CommandId.NewTab, "Tab", "Mở tab mới", "Ctrl+T"),
        new(CommandId.CloseTab, "Tab", "Đóng tab", "Ctrl+W"),
        new(CommandId.ReopenTab, "Tab", "Mở lại tab vừa đóng", "Ctrl+Shift+T"),
        new(CommandId.DuplicateTab, "Tab", "Nhân bản tab", "Ctrl+Shift+D"),
        new(CommandId.NextTab, "Tab", "Tab kế tiếp", "Ctrl+Tab"),
        new(CommandId.PreviousTab, "Tab", "Tab trước đó", "Ctrl+Shift+Tab"),
        new(CommandId.Address, "Điều hướng", "Chuyển tới ô địa chỉ", "Ctrl+L", "Alt+D"),
        new(CommandId.Back, "Điều hướng", "Quay lại", "Alt+Left"),
        new(CommandId.Forward, "Điều hướng", "Tiến tới", "Alt+Right"),
        new(CommandId.Up, "Điều hướng", "Lên thư mục cha", "Alt+Up"),
        new(CommandId.Refresh, "Điều hướng", "Làm mới", "F5"),
        new(CommandId.Favorites, "Điều hướng", "Mở danh sách yêu thích và gần đây", "Ctrl+B"),
        new(CommandId.AddFavorite, "Điều hướng", "Thêm / bỏ thư mục hiện tại khỏi yêu thích", "Ctrl+Shift+B"),
        new(CommandId.Search, "Tìm kiếm", "Tìm với Everything", "Ctrl+F"),
        new(CommandId.Filter, "Tìm kiếm", "Lọc nhanh thư mục hiện tại", "Ctrl+E"),
        new(CommandId.CopyToOther, "File", "Sao chép mục chọn sang panel kia", "Shift+F5"),
        new(CommandId.MoveToOther, "File", "Di chuyển mục chọn sang panel kia", "Shift+F6"),
        new(CommandId.OpenTerminal, "Công cụ", "Mở Terminal tại thư mục hiện tại", "Ctrl+OemTilde"),
        new(CommandId.ToggleTree, "Hiển thị", "Hiện / ẩn cây thư mục"),
        new(CommandId.TogglePreview, "Hiển thị", "Hiện / ẩn khung xem trước", "Alt+P"),
        new(CommandId.LayoutQuad, "Hiển thị", "Bố cục 4 khung"),
        new(CommandId.LayoutTriple, "Hiển thị", "Bố cục 3 khung"),
        new(CommandId.LayoutDual, "Hiển thị", "Bố cục 2 khung"),
        new(CommandId.LayoutSingle, "Hiển thị", "Bố cục 1 khung"),
        new(CommandId.Equalize, "Hiển thị", "Chia đều các panel"),
        new(CommandId.ToggleTheme, "Hiển thị", "Đổi chủ đề sáng / tối"),
        new(CommandId.SaveWorkspace, "Workspace", "Lưu workspace hiện tại", "Ctrl+Shift+S"),
        new(CommandId.CommandPalette, "Ứng dụng", "Mở bảng lệnh", "Ctrl+Shift+P"),
        new(CommandId.Settings, "Ứng dụng", "Mở Cài đặt", "Ctrl+OemComma"),
    ];

    public static CommandInfo Get(CommandId id) => All.First(c => c.Id == id);

    public static KeyChord[] DefaultGestures(CommandId id) =>
        Get(id).DefaultGestures.Select(KeyChord.Parse).OfType<KeyChord>().ToArray();

    /// <summary>Phím tắt hiện hành của một lệnh: theo layout.json nếu người dùng đã đổi, không thì mặc định.</summary>
    public static KeyChord[] Gestures(LayoutData settings, CommandId id) =>
        settings.Shortcuts.TryGetValue(id.ToString(), out var custom)
            ? custom.Select(KeyChord.Parse).OfType<KeyChord>().ToArray()
            : DefaultGestures(id);

    public static string GestureText(LayoutData settings, CommandId id) =>
        string.Join("  /  ", Gestures(settings, id).Select(g => g.ToString()));

    public static Dictionary<KeyChord, CommandId> BuildMap(LayoutData settings)
    {
        var map = new Dictionary<KeyChord, CommandId>();
        foreach (var command in All)
            foreach (var gesture in Gestures(settings, command.Id))
                map.TryAdd(gesture, command.Id);
        return map;
    }

    /// <summary>Gán phím cho lệnh; gỡ phím đó khỏi lệnh khác nếu trùng. Trả về lệnh bị gỡ (nếu có).</summary>
    public static CommandId? Assign(LayoutData settings, CommandId id, KeyChord? gesture)
    {
        CommandId? removedFrom = null;
        if (gesture is { } g)
        {
            foreach (var other in All.Where(c => c.Id != id))
            {
                var current = Gestures(settings, other.Id);
                if (!current.Contains(g)) continue;
                SetGestures(settings, other.Id, current.Where(x => x != g));
                removedFrom = other.Id;
            }
        }
        SetGestures(settings, id, gesture is { } chord ? [chord] : []);
        return removedFrom;
    }

    public static void Reset(LayoutData settings, CommandId id) => settings.Shortcuts.Remove(id.ToString());

    private static void SetGestures(LayoutData settings, CommandId id, IEnumerable<KeyChord> gestures)
    {
        var list = gestures.ToArray();
        if (list.SequenceEqual(DefaultGestures(id))) settings.Shortcuts.Remove(id.ToString());
        else settings.Shortcuts[id.ToString()] = list.Select(g => g.Serialize()).ToArray();
    }

    /// <summary>So khớp không dấu, không phân biệt hoa thường: "mo tab" khớp "Mở tab mới".</summary>
    public static string Fold(string text)
    {
        var normalized = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    internal static void SelfCheck()
    {
        foreach (var command in All)
            foreach (var text in command.DefaultGestures)
                if (KeyChord.Parse(text) is not { } chord || KeyChord.Parse(chord.Serialize()) != chord)
                    throw new InvalidOperationException($"Phím tắt mặc định không hợp lệ: {text}");
        if (KeyChord.FromVirtualKey(0x54) != Key.T || KeyChord.FromVirtualKey(0x74) != Key.F5 ||
            KeyChord.FromVirtualKey(0x32) != Key.D2 || KeyChord.FromVirtualKey(0xBC) != Key.OemComma)
            throw new InvalidOperationException("Đổi virtual-key sang Key sai.");
        var settings = new LayoutData();
        if (Assign(settings, CommandId.NewTab, KeyChord.Parse("Ctrl+W")) != CommandId.CloseTab ||
            Gestures(settings, CommandId.CloseTab).Length != 0 || BuildMap(settings)[new KeyChord(Key.W, KeyModifiers.Control)] != CommandId.NewTab)
            throw new InvalidOperationException("Gán phím trùng không gỡ khỏi lệnh cũ.");
        if (Fold("Mở Tab Đóng") != "mo tab dong") throw new InvalidOperationException("Fold sai.");
    }
}
