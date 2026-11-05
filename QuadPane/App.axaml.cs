using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using QuadPane.Shell;

namespace QuadPane;

public partial class App : Application
{
    internal static readonly string CrashLogPath = Path.Combine(Path.GetTempPath(), "QuadPane-crash.log");
    public static bool IsExiting { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var layout = LayoutStore.Load();
            ApplyThemeMode(layout.Theme);
            desktop.MainWindow = new MainWindow(layout);
            if (Program.InitialFolder is { } folder) ((MainWindow)desktop.MainWindow).OpenFolder(folder);
            desktop.ShutdownRequested += (_, _) => IsExiting = true;
            Program.StartActivationServer();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Đổi theme cho cả app (Avalonia + menu shell của Windows).</summary>
    public void ApplyThemeMode(ThemeMode mode)
    {
        RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        ShellTheme.SetProcessMode(mode);
    }

    public static void LogCrash(Exception? ex)
    {
        try { File.AppendAllText(CrashLogPath, $"[{DateTime.Now:O}] {ex}\n\n"); } catch { }
    }

    public static void RequestExit() => IsExiting = true;

    private void ShowMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }) return;
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void TrayIcon_Clicked(object? sender, EventArgs e) => ShowMainWindow();
    private void TrayOpen_Clicked(object? sender, EventArgs e) => ShowMainWindow();
    private void TrayExit_Clicked(object? sender, EventArgs e)
    {
        RequestExit();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }

    internal static void ActivateExisting(string? folder = null)
    {
        if (Current is not App app) return;
        if (folder is not null && app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
            window.OpenFolder(folder);
        app.ShowMainWindow();
    }
}
