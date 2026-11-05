using System.Runtime.InteropServices;
using static QuadPane.Shell.NativeMethods;

namespace QuadPane.Shell;

internal static class ShellContextMenu
{
    private const uint TpmReturnCommand = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const uint CmfExtendedVerbs = 0x0100;
    private const int SwShowNormal = 1;

    public static void Show(IntPtr owner, string path)
    {
        if (owner == IntPtr.Zero || !OperatingSystem.IsWindows()) return;
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Mục này không còn tồn tại.", path);

        var pidl = ParsePath(path);
        if (pidl == IntPtr.Zero) return;
        IntPtr folderPtr = IntPtr.Zero, menuPtr = IntPtr.Zero;
        IntPtr menu = IntPtr.Zero;
        object? context = null;
        try
        {
            var folderId = typeof(IShellFolder).GUID;
            Marshal.ThrowExceptionForHR(SHBindToParent(pidl, ref folderId, out folderPtr, out var child));
            var folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
            try
            {
                var menuId = typeof(IContextMenu).GUID;
                Marshal.ThrowExceptionForHR(folder.GetUIObjectOf(owner, 1, ref child, ref menuId,
                    IntPtr.Zero, out menuPtr));
            }
            finally { Marshal.ReleaseComObject(folder); }

            context = Marshal.GetObjectForIUnknown(menuPtr);
            var commands = (IContextMenu)context;
            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) throw new InvalidOperationException("Không tạo được menu Windows.");
            uint flags = (GetKeyState(VK_SHIFT) & 0x8000) != 0 ? CmfExtendedVerbs : 0;
            Marshal.ThrowExceptionForHR(commands.QueryContextMenu(menu, 0, 1, 0x7fff, flags));

            var context2 = context as IContextMenu2;
            var context3 = context as IContextMenu3;
            SubclassProc? callback = null;
            if (context2 is not null || context3 is not null)
            {
                callback = (hwnd, message, wParam, lParam, id, data) =>
                {
                    if (message is 0x0117 or 0x002b or 0x002c or 0x0120)
                    {
                        if (context3 is not null && context3.HandleMenuMsg2(message, wParam, lParam, out var result) >= 0)
                            return result;
                        if (context2 is not null && context2.HandleMenuMsg(message, wParam, lParam) >= 0)
                            return IntPtr.Zero;
                    }
                    return DefSubclassProc(hwnd, message, wParam, lParam);
                };
                if (!SetWindowSubclass(owner, callback, UIntPtr.Zero, IntPtr.Zero))
                    throw new InvalidOperationException("Không cài được hook cho menu Windows.");
            }
            try
            {
                GetCursorPos(out var point);
                SetForegroundWindow(owner);
                int command = TrackPopupMenuEx(menu, TpmReturnCommand | TpmRightButton,
                    point.X, point.Y, owner, IntPtr.Zero);
                if (command > 0)
                {
                    var info = new CMINVOKECOMMANDINFO
                    {
                        Size = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                        Owner = owner,
                        Verb = (IntPtr)(command - 1),
                        Show = SwShowNormal,
                    };
                    Marshal.ThrowExceptionForHR(commands.InvokeCommand(ref info));
                }
                PostMessage(owner, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                if (callback is not null) RemoveWindowSubclass(owner, callback, UIntPtr.Zero);
                GC.KeepAlive(callback);
            }
        }
        finally
        {
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            if (context is not null) Marshal.ReleaseComObject(context);
            if (menuPtr != IntPtr.Zero) Marshal.Release(menuPtr);
            if (folderPtr != IntPtr.Zero) Marshal.Release(folderPtr);
            ILFree(pidl);
        }
    }

    internal static void SelfCheck()
    {
        int expected = IntPtr.Size == 8 ? 56 : 36;
        if (Marshal.SizeOf<CMINVOKECOMMANDINFO>() != expected ||
            Marshal.SizeOf<ShellFileIcon.BITMAPINFOHEADER>() != 40)
            throw new InvalidOperationException("Kích thước Win32 interop không đúng.");
        ShellFileIcon.SelfCheck();
        if (!OperatingSystem.IsWindows()) return;

        var path = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var pidl = ParsePath(path);
        if (pidl == IntPtr.Zero) throw new InvalidOperationException("Không phân tích được PIDL Windows.");
        IntPtr folderPtr = IntPtr.Zero, menuPtr = IntPtr.Zero;
        try
        {
            var folderId = typeof(IShellFolder).GUID;
            Marshal.ThrowExceptionForHR(SHBindToParent(pidl, ref folderId, out folderPtr, out var child));
            var folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
            try
            {
                var menuId = typeof(IContextMenu).GUID;
                Marshal.ThrowExceptionForHR(folder.GetUIObjectOf(IntPtr.Zero, 1, ref child, ref menuId,
                    IntPtr.Zero, out menuPtr));
            }
            finally { Marshal.ReleaseComObject(folder); }
            if (menuPtr == IntPtr.Zero) throw new InvalidOperationException("Windows Shell không trả context menu.");
        }
        finally
        {
            if (menuPtr != IntPtr.Zero) Marshal.Release(menuPtr);
            if (folderPtr != IntPtr.Zero) Marshal.Release(folderPtr);
            ILFree(pidl);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CMINVOKECOMMANDINFO
    {
        public int Size;
        public uint Mask;
        public IntPtr Owner;
        public IntPtr Verb;
        public IntPtr Parameters;
        public IntPtr Directory;
        public int Show;
        public uint HotKey;
        public IntPtr Icon;
    }

    [ComImport, Guid("000214e6-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr bind, [MarshalAs(UnmanagedType.LPWStr)] string name,
            out uint eaten, out IntPtr pidl, ref uint attributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint flags, out IntPtr enumerator);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr bind, ref Guid iid, out IntPtr result);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr bind, ref Guid iid, out IntPtr result);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr first, IntPtr second);
        [PreserveSig] int CreateViewObject(IntPtr hwnd, ref Guid iid, out IntPtr result);
        [PreserveSig] int GetAttributesOf(uint count, ref IntPtr pidl, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(IntPtr hwnd, uint count, ref IntPtr pidl, ref Guid iid,
            IntPtr reserved, out IntPtr result);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint position, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO info);
        [PreserveSig] int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr buffer, uint length);
    }

    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2 : IContextMenu
    {
        [PreserveSig] new int QueryContextMenu(IntPtr menu, uint position, uint first, uint last, uint flags);
        [PreserveSig] new int InvokeCommand(ref CMINVOKECOMMANDINFO info);
        [PreserveSig] new int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr buffer, uint length);
        [PreserveSig] int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3 : IContextMenu2
    {
        [PreserveSig] new int QueryContextMenu(IntPtr menu, uint position, uint first, uint last, uint flags);
        [PreserveSig] new int InvokeCommand(ref CMINVOKECOMMANDINFO info);
        [PreserveSig] new int GetCommandString(UIntPtr command, uint type, IntPtr reserved, IntPtr buffer, uint length);
        [PreserveSig] new int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam,
        UIntPtr id, IntPtr data);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid iid, out IntPtr folder, out IntPtr child);
    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr reserved);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id, IntPtr data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id);
    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
