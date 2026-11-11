using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using QuadPane.Shell;

namespace QuadPane;

/// <summary>
/// Đại diện cho một node (ổ đĩa, thư mục, tệp) trong cây phân tích dung lượng TreeSize.
/// </summary>
public sealed class TreeSizeItem : INotifyPropertyChanged
{
    private string _name = "";
    private long _size;
    private long _parentSize;
    private bool _isLoading;
    private bool _isLoaded;
    private bool _isExpanded;
    private int _fileCount;
    private int _folderCount;

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public string FullPath { get; set; } = "";
    public bool IsDrive { get; set; }
    public bool IsFolder { get; set; }

    public bool CanDeleteFolder => IsFolder && !IsDrive && Path.IsPathFullyQualified(FullPath) &&
        new DirectoryInfo(FullPath).Parent is not null;

    internal bool RemoveDescendant(TreeSizeItem target)
    {
        if (!Children.Remove(target) && !Children.Any(child => child.RemoveDescendant(target))) return false;
        Size = Math.Max(0, Size - target.Size);
        FolderCount = Children.Count(child => child.IsFolder);
        foreach (var child in Children) child.ParentSize = Size;
        return true;
    }

    public long Size
    {
        get => _size;
        set
        {
            if (_size != value)
            {
                _size = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SizeText));
                OnPropertyChanged(nameof(Percent));
                OnPropertyChanged(nameof(PercentText));
            }
        }
    }

    public long ParentSize
    {
        get => _parentSize;
        set
        {
            if (_parentSize != value)
            {
                _parentSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Percent));
                OnPropertyChanged(nameof(PercentText));
            }
        }
    }

    public double Percent => ParentSize > 0 ? Math.Clamp((double)Size / ParentSize * 100.0, 0.0, 100.0) : 0;
    public string SizeText => SearchResult.FormatBytes(Size);
    public string PercentText => $"{Percent:F1}%";

    public int FileCount
    {
        get => _fileCount;
        set { if (_fileCount != value) { _fileCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileCountText)); } }
    }

    public int FolderCount
    {
        get => _folderCount;
        set { if (_folderCount != value) { _folderCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(FolderCountText)); } }
    }

    public string FileCountText => (IsFolder || IsDrive) && FileCount > 0 ? $"{FileCount:N0} tệp" : "";
    public string FolderCountText => (IsFolder || IsDrive) && FolderCount > 0 ? $"{FolderCount:N0} thư mục" : "";

    public DateTime? Modified { get; set; }
    public string ModifiedText => Modified?.ToString("yyyy/MM/dd HH:mm") ?? "";

    public Bitmap? IconSource => ShellFileIcon.Get(FullPath, IsFolder || IsDrive);

    public bool IsLoading
    {
        get => _isLoading;
        set { if (_isLoading != value) { _isLoading = value; OnPropertyChanged(); } }
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        set { if (_isLoaded != value) { _isLoaded = value; OnPropertyChanged(); } }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<TreeSizeItem> Children { get; } = new();

    public bool HasDummyChild => Children.Count == 1 && string.IsNullOrEmpty(Children[0].FullPath);

    internal static void SelfCheck()
    {
        var item = new TreeSizeItem
        {
            Name = "Test",
            FullPath = @"C:\Test",
            Size = 1024 * 1024,
            ParentSize = 10 * 1024 * 1024,
        };
        var rootPath = Path.GetPathRoot(Path.GetFullPath("."))!;
        var folder = new TreeSizeItem { IsFolder = true, FullPath = Path.Combine(rootPath, "example"), Size = 40 };
        var root = new TreeSizeItem { IsFolder = true, IsDrive = true, FullPath = rootPath, Size = 100 };
        var sibling = new TreeSizeItem { Size = 60, ParentSize = 100 };
        root.Children.Add(folder);
        root.Children.Add(sibling);
        if (root.CanDeleteFolder || !folder.CanDeleteFolder || new TreeSizeItem().CanDeleteFolder ||
            new TreeSizeItem { IsFolder = true, FullPath = rootPath }.CanDeleteFolder ||
            new TreeSizeItem { IsFolder = true, FullPath = Path.Combine(rootPath, "example", "..") }.CanDeleteFolder ||
            new TreeSizeItem { FullPath = folder.FullPath }.CanDeleteFolder)
            throw new InvalidOperationException("TreeSize deletion eligibility failed.");
        if (!root.RemoveDescendant(folder) || root.Size != 60 || sibling.ParentSize != 60 ||
            root.Children.Contains(folder) || root.RemoveDescendant(folder))
            throw new InvalidOperationException("TreeSize deletion totals failed.");

        var nested = new TreeSizeItem { IsFolder = true, Size = 20 };
        var parent = new TreeSizeItem { IsFolder = true, Size = 30 };
        parent.Children.Add(nested);
        root.Children.Add(parent);
        root.Size = 90;
        if (!root.RemoveDescendant(nested) || parent.Size != 10 || root.Size != 70 ||
            parent.ParentSize != 70 || sibling.ParentSize != 70)
            throw new InvalidOperationException("TreeSize nested deletion totals failed.");

        if (item.SizeText != "1.0 MB" || item.PercentText != "10.0%")
            throw new InvalidOperationException($"TreeSizeItem calculations failed: SizeText={item.SizeText}, PercentText={item.PercentText}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
