using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuadPane;

public enum LayoutMode { Quad, Triple, Dual, Single }

public enum ThemeMode { System, Light, Dark }

/// <summary>
/// Trạng thái lưu giữa các lần chạy, tương tự file .qdr của Q-Dir
/// (Q-Dir lưu SP0/SP1/SPT, Tree1..4, Focus, colorthema... dạng INI; ở đây dùng JSON).
/// </summary>
public sealed class LayoutData
{
    public double LeftColumn { get; set; } = 1;
    public double RightColumn { get; set; } = 1;
    public double TopRow { get; set; } = 1;
    public double BottomRow { get; set; } = 1;
    public LayoutMode Mode { get; set; } = LayoutMode.Quad;
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public int ActivePane { get; set; }
    public string?[] Paths { get; set; } = new string?[4];
    public string?[][] TabPaths { get; set; } = new string?[4][];
    public string?[][] TabHomes { get; set; } = new string?[4][];
    public string[][] TabIds { get; set; } = new string[4][];
    public bool[][] TabPinned { get; set; } = new bool[4][];
    public int[] ActiveTabs { get; set; } = new int[4];
    public bool[] ShowTree { get; set; } = new bool[4];
    public bool[] ShowPreview { get; set; } = new bool[4];

    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool Maximized { get; set; }

    // ---- Cài đặt ----
    public bool CloseToTray { get; set; } = true;
    public string? NewTabPath { get; set; }
    public bool ShowGitBar { get; set; } = true;
    public bool ShowClaudeButton { get; set; } = true;
    public bool ShowCodexButton { get; set; } = true;
    public bool ShowVSCodeButton { get; set; } = true;
    public bool ShowTerminalButton { get; set; } = true;
    public bool UseWindowsTerminal { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>Phím tắt người dùng đã đổi: tên lệnh → các tổ hợp phím. Lệnh không có ở đây dùng mặc định.</summary>
    public Dictionary<string, string[]> Shortcuts { get; set; } = new();
    public List<string> Favorites { get; set; } = [];
    public List<string> RecentFolders { get; set; } = [];
    public List<Workspace> Workspaces { get; set; } = [];

    public const int MaxRecentFolders = 20;

    public void AddRecent(string path)
    {
        RecentFolders.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, path);
        if (RecentFolders.Count > MaxRecentFolders) RecentFolders.RemoveRange(MaxRecentFolders, RecentFolders.Count - MaxRecentFolders);
    }

    public bool IsFavorite(string? path) =>
        path is not null && Favorites.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Thư mục mở khi tạo tab mới; mặc định là C:\.</summary>
    [JsonIgnore]
    public string EffectiveNewTabPath =>
        !string.IsNullOrWhiteSpace(NewTabPath) && Directory.Exists(NewTabPath) ? NewTabPath : @"C:\";
}

/// <summary>Bộ tab và bố cục có tên, chuyển qua lại nhanh (giống file .qdr của Q-Dir).</summary>
public sealed class Workspace
{
    public string Name { get; set; } = "";
    public LayoutMode Mode { get; set; } = LayoutMode.Quad;
    public int ActivePane { get; set; }
    public string?[][] TabPaths { get; set; } = new string?[4][];
    public string?[][] TabHomes { get; set; } = new string?[4][];
    public bool[][] TabPinned { get; set; } = new bool[4][];
    public int[] ActiveTabs { get; set; } = new int[4];
    public bool[] ShowTree { get; set; } = new bool[4];
    public bool[] ShowPreview { get; set; } = new bool[4];
}

public static class LayoutStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuadPane", "layout.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string[] DefaultPaths() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", // This PC
    ];

    public static string DefaultPath(int index) => DefaultPaths()[index];

    internal static void SelfCheckTabHomes()
    {
        var source = new LayoutData { TabHomes = [[@"C:\Home", null], [], [], []] };
        var restored = JsonSerializer.Deserialize<LayoutData>(JsonSerializer.Serialize(source, Options), Options);
        if (restored?.TabHomes[0] is not [@"C:\Home", null] || restored.TabHomes[1].Length != 0)
            throw new InvalidOperationException("Không lưu/khôi phục được thư mục home của từng tab.");
    }

    public static LayoutData Load()
    {
        LayoutData? data = null;
        try
        {
            if (File.Exists(FilePath))
                data = JsonSerializer.Deserialize<LayoutData>(File.ReadAllText(FilePath), Options);
        }
        catch (Exception) { /* file hỏng hoặc định dạng cũ: dùng mặc định */ }

        data ??= new LayoutData();
        if (data.ShowTree is not { Length: 4 }) data.ShowTree = new bool[4];
        if (data.Paths is not { Length: 4 }) data.Paths = new string?[4];
        if (data.TabPaths is not { Length: 4 }) data.TabPaths = new string?[4][];
        if (data.TabHomes is not { Length: 4 }) data.TabHomes = new string?[4][];
        if (data.TabIds is not { Length: 4 }) data.TabIds = new string[4][];
        if (data.TabPinned is not { Length: 4 }) data.TabPinned = new bool[4][];
        if (data.ActiveTabs is not { Length: 4 }) data.ActiveTabs = new int[4];
        if (data.ShowPreview is not { Length: 4 }) data.ShowPreview = new bool[4];
        data.Shortcuts ??= new();
        data.Favorites ??= [];
        data.RecentFolders ??= [];
        data.Workspaces ??= [];
        data.Workspaces.RemoveAll(w => w is null || w.TabPaths is not { Length: 4 } || string.IsNullOrWhiteSpace(w.Name));
        foreach (var w in data.Workspaces)
        {
            if (w.TabHomes is not { Length: 4 }) w.TabHomes = new string?[4][];
            if (w.TabPinned is not { Length: 4 }) w.TabPinned = new bool[4][];
            if (w.ActiveTabs is not { Length: 4 }) w.ActiveTabs = new int[4];
            if (w.ShowTree is not { Length: 4 }) w.ShowTree = new bool[4];
            if (w.ShowPreview is not { Length: 4 }) w.ShowPreview = new bool[4];
        }

        // Panel nào chưa có đường dẫn thì dùng thư mục mặc định
        var defaults = DefaultPaths();
        for (int i = 0; i < 4; i++)
        {
            if (string.IsNullOrWhiteSpace(data.Paths[i])) data.Paths[i] = defaults[i];
            if (data.TabPaths[i] is not { Length: > 0 }) data.TabPaths[i] = [data.Paths[i]];
            if (data.TabHomes[i] is not { } homes || homes.Length != data.TabPaths[i].Length)
                data.TabHomes[i] = new string?[data.TabPaths[i].Length];
            if (data.TabPinned[i] is not { } pins || pins.Length != data.TabPaths[i].Length)
                data.TabPinned[i] = new bool[data.TabPaths[i].Length];
            var ids = data.TabIds[i];
            if (ids is not { Length: > 0 } || ids.Length != data.TabPaths[i].Length)
                ids = new string[data.TabPaths[i].Length];
            for (int j = 0; j < ids.Length; j++)
                if (string.IsNullOrWhiteSpace(ids[j]))
                    ids[j] = j == 0 ? $"QuadPane.Pane{i}" : $"QuadPane.Pane{i}.Tab{Guid.NewGuid():N}";
            data.TabIds[i] = ids;
            data.ActiveTabs[i] = Math.Clamp(data.ActiveTabs[i], 0, data.TabPaths[i].Length - 1);
        }
        return data;
    }

    public static string FolderPath => Path.GetDirectoryName(FilePath)!;

    public static void Save(LayoutData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, Options));
        }
        catch (Exception) { /* không lưu được thì bỏ qua */ }
    }
}
