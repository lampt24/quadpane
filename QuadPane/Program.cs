using Avalonia;

namespace QuadPane;

internal static class Program
{
    // ExplorerBrowser là COM và yêu cầu thread STA.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => App.LogCrash(e.ExceptionObject as Exception);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

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
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
