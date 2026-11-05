using System.Runtime.InteropServices;
using System.Text;

namespace QuadPane;

public sealed record SearchResult(string Name, string Directory, string FullPath, bool IsFolder)
{
    public string Kind => IsFolder ? "Thư mục" : "Tệp";
    public string Icon => IsFolder ? "\uE8B7" : "\uE8A5";
}
public sealed record SearchResponse(IReadOnlyList<SearchResult> Results, uint Total);

/// <summary>Gọi Everything SDK qua IPC. Everything phải đang chạy; DLL đi kèm chỉ là client.</summary>
public static class EverythingSearch
{
    private const uint MaxResults = 200;
    private const uint RequestFullPath = 0x00000004;
    private static readonly object Gate = new(); // SDK giữ trạng thái truy vấn toàn cục

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
            Native.SetRequestFlags(RequestFullPath);
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
                results.Add(new SearchResult(name, directory, fullPath, Native.IsFolderResult(i)));
            }
            return new SearchResponse(results, Native.GetTotResults());
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
    }
}
