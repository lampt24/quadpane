using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace QuadPane.Shell;

internal static class ShellFileIcon
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? Get(string path, bool folder)
    {
        if (!OperatingSystem.IsWindows()) return null;
        string key = folder ? "folder" : Path.GetExtension(path);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        Bitmap? icon;
        try { icon = Load(path, folder); }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or DllNotFoundException)
        {
            icon = null;
        }
        if (Cache.Count < 128) Cache[key] = icon;
        return icon;
    }

    internal static void SelfCheck()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (SHGetFileInfo(path, 0x10, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(),
            0x100 | 0x1 | 0x10) == IntPtr.Zero || info.Icon == IntPtr.Zero)
            throw new InvalidOperationException("Windows Shell không trả icon thư mục.");
        DestroyIcon(info.Icon);
    }

    private static Bitmap? Load(string path, bool folder)
    {
        const int size = 16;
        const uint flags = 0x100 | 0x1 | 0x10; // SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES
        if (SHGetFileInfo(path, folder ? 0x10u : 0x80u, out var info,
            (uint)Marshal.SizeOf<SHFILEINFO>(), flags) == IntPtr.Zero || info.Icon == IntPtr.Zero) return null;
        IntPtr dc = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            dc = CreateCompatibleDC(IntPtr.Zero);
            var bitmapInfo = new BITMAPINFO
            {
                Header = new BITMAPINFOHEADER
                {
                    Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    Width = size,
                    Height = -size,
                    Planes = 1,
                    BitCount = 32,
                },
            };
            dib = CreateDIBSection(dc, ref bitmapInfo, 0, out var pixels, IntPtr.Zero, 0);
            if (dc == IntPtr.Zero || dib == IntPtr.Zero || pixels == IntPtr.Zero) return null;
            old = SelectObject(dc, dib);
            if (old == IntPtr.Zero || !DrawIconEx(dc, 0, 0, info.Icon, size, size, 0, IntPtr.Zero, 3)) return null;
            var bytes = new byte[size * size * 4];
            Marshal.Copy(pixels, bytes, 0, bytes.Length);
            // Một số icon cũ không ghi alpha khi vẽ lên DIB 32-bit.
            for (int i = 0; i < bytes.Length; i += 4)
                if (bytes[i + 3] == 0 && (bytes[i] | bytes[i + 1] | bytes[i + 2]) != 0)
                    bytes[i + 3] = 255;
            var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var buffer = bitmap.Lock())
                for (int row = 0; row < size; row++)
                    Marshal.Copy(bytes, row * size * 4, IntPtr.Add(buffer.Address, row * buffer.RowBytes), size * 4);
            return bitmap;
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(dc, old);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            DestroyIcon(info.Icon);
        }
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint ImageSize;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER Header;
        public uint Colors0;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, out SHFILEINFO info,
        uint size, uint flags);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, uint usage,
        out IntPtr pixels, IntPtr section, uint offset);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr bitmap);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr bitmap);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height,
        uint step, IntPtr brush, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
