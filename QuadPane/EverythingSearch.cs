using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Media.Imaging;
using QuadPane.Shell;

namespace QuadPane;

public sealed record SearchResult(string Name, string Directory, string FullPath, bool IsFolder,
    DateTime? Modified, long? Size, string TypeName)
{
    public string Icon => IsFolder ? "\uE8B7" : "\uE8A5";
    public Bitmap? IconSource => ShellFileIcon.Get(FullPath, IsFolder);
    public string ModifiedText => Modified?.ToString("yyyy/MM/dd HH:mm") ?? "";
    public string SizeText => Size is long size && size >= 0 && !IsFolder
        ? $"{size / 1024 + (size % 1024 == 0 ? 0 : 1):N0} KB" : "";
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
            new DateTime(2026, 11, 5, 14, 15, 0), 7168, "Markdown file");
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
                results.Add(new SearchResult(name, directory, fullPath, isFolder, modified, size,
                    GetTypeName(fullPath, isFolder)));
            }
            return new SearchResponse(results, Native.GetTotResults());
        }
    }

    private static string GetTypeName(string path, bool isFolder)
    {
        uint attributes = isFolder ? 0x10u : 0x80u;
        if (SHGetFileInfo(path, attributes, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x410) != IntPtr.Zero &&
            !string.IsNullOrWhiteSpace(info.TypeName)) return info.TypeName;
        return isFolder ? "File folder" : (Path.GetExtension(path).TrimStart('.') is { Length: > 0 } ext
            ? $"{ext.ToUpperInvariant()} file" : "File");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, out SHFILEINFO info,
        uint infoSize, uint flags);

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
