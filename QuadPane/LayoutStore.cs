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
    public bool[] ShowTree { get; set; } = new bool[4];

    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool Maximized { get; set; }
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

        // Panel nào chưa có đường dẫn thì dùng thư mục mặc định
        var defaults = DefaultPaths();
        for (int i = 0; i < 4; i++)
            if (string.IsNullOrWhiteSpace(data.Paths[i])) data.Paths[i] = defaults[i];
        return data;
    }

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
