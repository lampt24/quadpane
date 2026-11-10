using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Media.Imaging;
using QuadPane.Shell;

namespace QuadPane;

public sealed record SearchResult(string Name, string Directory, string FullPath, bool IsFolder,
    DateTime? Modified, long? Size)
{
    public string Icon => IsFolder ? "\uE8B7" : "\uE8A5";
    public Bitmap? IconSource => ShellFileIcon.Get(FullPath, IsFolder);
    public string ModifiedText => Modified?.ToString("yyyy/MM/dd HH:mm") ?? "";
    public string SizeText => Size is long size && size >= 0 && (!IsFolder || size > 0)
        ? FormatBytes(size) : "";

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{Math.Ceiling(kb):N0} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F1} MB";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB";
    }
}
public sealed record SearchResponse(IReadOnlyList<SearchResult> Results, uint Total);

/// <summary>Gọi Everything SDK qua IPC. Everything phải đang chạy; DLL đi kèm chỉ là client.</summary>
public static class EverythingSearch
{
    private const uint MaxResults = 200;
    private const uint RequestFullPath = 0x00000004;
    private const uint RequestSize = 0x00000010;
    private const uint RequestDateModified = 0x00000040;
    private static readonly object Gate = new(); // SDK giữ trạng thái truy vấn toàn cục

    internal static void SelfCheck()
    {
        var item = new SearchResult("readme.md", @"C:\work", @"C:\work\readme.md", false,
            new DateTime(2026, 11, 5, 14, 15, 0), 7168);
        if (item.ModifiedText != "2026/11/05 14:15" || item.SizeText != "7 KB")
            throw new InvalidOperationException("Search metadata formatting failed.");
    }

    static EverythingSearch()
    {
        NativeLibrary.SetDllImportResolver(typeof(EverythingSearch).Assembly, (name, assembly, path) =>
        {
            if (name != "EverythingSdk") return IntPtr.Zero;
            string file = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => "Everything32.dll",
                Architecture.X64 => "Everything64.dll",
                Architecture.Arm64 => "EverythingARM64.dll",
                _ => throw new PlatformNotSupportedException("Everything SDK không hỗ trợ kiến trúc này."),
            };
            return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "EverythingSdk", file));
        });
    }

    public static SearchResponse Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new SearchResponse([], 0);
        lock (Gate)
        {
            Native.Reset();
            Native.SetSearch(query.Trim());
            Native.SetRequestFlags(RequestFullPath | RequestSize | RequestDateModified);
            Native.SetMax(MaxResults);
            if (!Native.Query(true))
            {
                uint error = Native.GetLastError();
                throw new InvalidOperationException(error == 2
                    ? "Everything chưa chạy. Hãy mở Everything rồi thử lại."
                    : $"Everything không thể tìm kiếm (mã lỗi {error}).");
            }

            uint count = Native.GetNumResults();
            var results = new List<SearchResult>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var buffer = new StringBuilder(32768);
                Native.GetResultFullPathName(i, buffer, (uint)buffer.Capacity);
                string fullPath = buffer.ToString();
                string name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
                string directory = Path.GetDirectoryName(fullPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
                bool isFolder = Native.IsFolderResult(i);
                DateTime? modified = null;
                if (Native.GetResultDateModified(i, out long filetime))
                {
                    try { modified = DateTime.FromFileTimeUtc(filetime).ToLocalTime(); }
                    catch (ArgumentOutOfRangeException) { }
                }
                long? size = Native.GetResultSize(i, out long bytes) ? bytes : null;
                results.Add(new SearchResult(name, directory, fullPath, isFolder, modified, size));
            }
            return new SearchResponse(results, Native.GetTotResults());
        }
    }

    /// <summary>
    /// Lấy danh sách các tệp và thư mục con trực tiếp của một thư mục thông qua Everything SDK.
    /// Trả về rỗng nếu Everything chưa chạy hoặc không tìm thấy kết quả.
    /// </summary>
    public static List<SearchResult> GetDirectChildren(string folderPath, uint maxResults = 10000)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return [];
        folderPath = folderPath.Trim();
        if (folderPath.Length > 3 && folderPath.EndsWith(Path.DirectorySeparatorChar))
            folderPath = folderPath.TrimEnd(Path.DirectorySeparatorChar);

        lock (Gate)
        {
            try
            {
                Native.Reset();
                Native.SetSearch($"parent:\"{folderPath}\"");
                Native.SetRequestFlags(RequestFullPath | RequestSize | RequestDateModified);
                Native.SetMax(maxResults);
                if (!Native.Query(true)) return [];

                uint count = Native.GetNumResults();
                var results = new List<SearchResult>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    var buffer = new StringBuilder(32768);
                    Native.GetResultFullPathName(i, buffer, (uint)buffer.Capacity);
                    string fullPath = buffer.ToString();
                    string name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
                    if (string.IsNullOrEmpty(name)) name = fullPath;
                    string directory = Path.GetDirectoryName(fullPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
                    bool isFolder = Native.IsFolderResult(i);
                    DateTime? modified = null;
                    if (Native.GetResultDateModified(i, out long filetime))
                    {
                        try { modified = DateTime.FromFileTimeUtc(filetime).ToLocalTime(); }
                        catch (ArgumentOutOfRangeException) { }
                    }
                    long? size = Native.GetResultSize(i, out long bytes) ? bytes : null;
                    results.Add(new SearchResult(name, directory, fullPath, isFolder, modified, size));
                }
                return results;
            }
            catch
            {
                return [];
            }
        }
    }

    /// <summary>
    /// Lấy dung lượng của một thư mục thông qua Everything SDK (đọc từ RAM index của Everything, 0 ms I/O).
    /// Trả về null nếu Everything chưa chạy, chưa index folder size hoặc không tìm thấy.
    /// </summary>
    public static long? GetFolderSize(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return null;
        folderPath = Path.TrimEndingDirectorySeparator(folderPath.Trim());

        lock (Gate)
        {
            try
            {
                Native.Reset();
                Native.SetSearch($"folder: exact:\"{folderPath}\"");
                Native.SetRequestFlags(RequestFullPath | RequestSize);
                Native.SetMax(1);
                if (!Native.Query(true)) return null;

                if (Native.GetNumResults() > 0 &&
                    Native.GetResultSize(0, out long size) && size >= 0)
                {
                    return size;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Lấy dung lượng của một tệp (từ hệ thống tệp) hoặc thư mục (từ Everything SDK).</summary>
    public static long? GetItemSize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (Directory.Exists(path)) return GetFolderSize(path);
        }
        catch { }
        return null;
    }

    /// <summary>Tính tổng dung lượng của danh sách các tệp/thư mục được chọn.</summary>
    public static (long TotalSize, int KnownCount) GetTotalSize(IReadOnlyList<string> paths)
    {
        long total = 0;
        int known = 0;
        foreach (var path in paths)
        {
            if (GetItemSize(path) is { } size)
            {
                total += size;
                known++;
            }
        }
        return (total, known);
    }

    /// <summary>Kiểm tra xem Everything có đang bật chỉ mục dung lượng thư mục (Index folder size) hay không.</summary>
    public static bool IsFolderSizeIndexed()
    {
        lock (Gate)
        {
            try
            {
                Native.Reset();
                Native.SetSearch("folder:");
                Native.SetRequestFlags(RequestSize);
                Native.SetMax(1);
                if (!Native.Query(true)) return false;
                if (Native.GetNumResults() > 0 && Native.GetResultSize(0, out long size))
                {
                    return size >= 0;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Tự động ghi cấu hình index_folder_size=1 vào Everything.ini và gửi lệnh cập nhật tới Everything đang chạy.</summary>
    public static bool TryEnableFolderSizeIndexing()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidates = new List<string>
            {
                Path.Combine(appData, "Everything", "Everything.ini"),
                Path.Combine(appData, "Everything", "Everything-1.5a.ini"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Everything", "Everything.ini")
            };

            bool modified = false;
            foreach (var iniPath in candidates)
            {
                if (!File.Exists(iniPath)) continue;
                var lines = File.ReadAllLines(iniPath).ToList();
                bool foundIndexFolderSize = false;
                bool foundFastSort = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith("index_folder_size=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = "index_folder_size=1";
                        foundIndexFolderSize = true;
                    }
                    else if (lines[i].StartsWith("fast_folder_size_sort=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = "fast_folder_size_sort=1";
                        foundFastSort = true;
                    }
                }
                if (!foundIndexFolderSize) lines.Add("index_folder_size=1");
                if (!foundFastSort) lines.Add("fast_folder_size_sort=1");
                File.WriteAllLines(iniPath, lines);
                modified = true;
            }

            var procs = Process.GetProcessesByName("Everything")
                .Concat(Process.GetProcessesByName("Everything64"));
            foreach (var p in procs)
            {
                try
                {
                    var exe = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                    {
                        using var proc = Process.Start(new ProcessStartInfo(exe, "-config-value index_folder_size=1 -config-value fast_folder_size_sort=1") { UseShellExecute = true });
                        modified = true;
                    }
                }
                catch { }
            }
            return modified;
        }
        catch
        {
            return false;
        }
    }

    private static class Native
    {
        [DllImport("EverythingSdk", EntryPoint = "Everything_Reset", CallingConvention = CallingConvention.StdCall)]
        internal static extern void Reset();
        [DllImport("EverythingSdk", EntryPoint = "Everything_SetSearchW", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        internal static extern void SetSearch(string query);
        [DllImport("EverythingSdk", EntryPoint = "Everything_SetRequestFlags", CallingConvention = CallingConvention.StdCall)]
        internal static extern void SetRequestFlags(uint flags);
        [DllImport("EverythingSdk", EntryPoint = "Everything_SetMax", CallingConvention = CallingConvention.StdCall)]
        internal static extern void SetMax(uint max);
        [DllImport("EverythingSdk", EntryPoint = "Everything_QueryW", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Query([MarshalAs(UnmanagedType.Bool)] bool wait);
        [DllImport("EverythingSdk", EntryPoint = "Everything_GetLastError", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint GetLastError();
        [DllImport("EverythingSdk", EntryPoint = "Everything_GetNumResults", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint GetNumResults();
        [DllImport("EverythingSdk", EntryPoint = "Everything_GetTotResults", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint GetTotResults();
        [DllImport("EverythingSdk", EntryPoint = "Everything_GetResultFullPathNameW", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        internal static extern uint GetResultFullPathName(uint index, StringBuilder buffer, uint capacity);
        [DllImport("EverythingSdk", EntryPoint = "Everything_IsFolderResult", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsFolderResult(uint index);
        [DllImport("EverythingSdk", EntryPoint = "Everything_GetResultSize", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetResultSize(uint index, out long size);
        [DllImport("EverythingSdk", EntryPoint = "Everything_GetResultDateModified", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetResultDateModified(uint index, out long filetime);
    }
}
