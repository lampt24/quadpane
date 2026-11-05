using Avalonia;
using Avalonia.Threading;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace QuadPane;

internal static class Program
{
    private static readonly string PipeName = $"QuadPane-{Process.GetCurrentProcess().SessionId}";
    private static readonly CancellationTokenSource ActivationStop = new();
    private static bool _primaryInstance;

    // ExplorerBrowser là COM và yêu cầu thread STA.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => App.LogCrash(e.ExceptionObject as Exception);
        if (!OperatingSystem.IsWindows())
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return;
        }

        using var mutex = new Mutex(true, @"Local\QuadPane", out var firstInstance);
        if (!firstInstance)
        {
            ActivateRunningInstance();
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
                if (await pipe.ReadAsync(message, ActivationStop.Token) == 1 && message[0] == 1)
                    Dispatcher.UIThread.Post(App.ActivateExisting);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                App.LogCrash(ex);
                await Task.Delay(500);
            }
        }
    }

    private static void ActivateRunningInstance()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(500);
                pipe.WriteByte(1);
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
