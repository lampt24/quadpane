using Avalonia;
using Avalonia.Threading;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace QuadPane;

internal static class Program
{
    private static readonly string PipeName = $"QuadPane-{Process.GetCurrentProcess().SessionId}";
    private static readonly CancellationTokenSource ActivationStop = new();
    private static bool _primaryInstance;
    internal static string? InitialFolder { get; private set; }

    // ExplorerBrowser là COM và yêu cầu thread STA.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["--check-git-status"]) { GitService.SelfCheckAsync().GetAwaiter().GetResult(); return; }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => App.LogCrash(e.ExceptionObject as Exception);
        if (!OperatingSystem.IsWindows())
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return;
        }

        if (args.Length > 0)
        {
            if (args.Length != 1 || !TryGetFolder(args[0], out var folder))
            {
                MessageBoxW(IntPtr.Zero, "Đường dẫn không phải thư mục hoặc file đang tồn tại.", "QuadPane", 0x10);
                return;
            }
            InitialFolder = folder;
        }

        using var mutex = new Mutex(true, @"Local\QuadPane", out var firstInstance);
        if (!firstInstance)
        {
            ActivateRunningInstance(InitialFolder);
            return;
        }
        _primaryInstance = true;
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally
        {
            ActivationStop.Cancel();
            mutex.ReleaseMutex();
        }
    }

    internal static void StartActivationServer()
    {
        if (_primaryInstance) _ = Task.Run(ListenForActivationAsync);
    }

    private static async Task ListenForActivationAsync()
    {
        while (!ActivationStop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ActivationStop.Token);
                var message = new byte[1];
                await pipe.ReadExactlyAsync(message, ActivationStop.Token);
                string? folder = null;
                if (message[0] == 2)
                {
                    var lengthBytes = new byte[4];
                    await pipe.ReadExactlyAsync(lengthBytes, ActivationStop.Token);
                    int length = BitConverter.ToInt32(lengthBytes);
                    if (length <= 0 || length > 32768) continue;
                    var pathBytes = new byte[length];
                    await pipe.ReadExactlyAsync(pathBytes, ActivationStop.Token);
                    if (!TryGetFolder(Encoding.UTF8.GetString(pathBytes), out folder)) continue;
                }
                else if (message[0] != 1) continue;
                Dispatcher.UIThread.Post(() => App.ActivateExisting(folder));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                App.LogCrash(ex);
                await Task.Delay(500);
            }
        }
    }

    private static bool TryGetFolder(string path, out string? folder)
    {
        folder = null;
        try
        {
            string full = Path.GetFullPath(path);
            if (Directory.Exists(full)) folder = full;
            else if (File.Exists(full)) folder = Path.GetDirectoryName(full);
            if (folder is not null) folder = Path.TrimEndingDirectorySeparator(folder);
        }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
        catch (PathTooLongException) { }
        return folder is not null;
    }

    private static void ActivateRunningInstance(string? folder)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(500);
                if (folder is null) pipe.WriteByte(1);
                else
                {
                    var bytes = Encoding.UTF8.GetBytes(folder);
                    pipe.WriteByte(2);
                    pipe.Write(BitConverter.GetBytes(bytes.Length));
                    pipe.Write(bytes);
                }
                return;
            }
            catch (IOException) { Thread.Sleep(100); }
            catch (TimeoutException) { Thread.Sleep(100); }
        }
        MessageBoxW(IntPtr.Zero, "QuadPane đang chạy nhưng không thể mở cửa sổ. Hãy kiểm tra biểu tượng ở khay hệ thống.",
            "QuadPane", 0x10);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint options);

    // Dùng bởi visual designer, đừng xoá.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // ANGLE (DirectX) là backend ổn định nhất trên Windows; Software làm dự phòng.
            // Không thử Vulkan để tránh phụ thuộc driver khi nhúng cửa sổ Win32 (Explorer).
            .With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
            })
            .WithInterFont()
            .LogToTrace();
}
