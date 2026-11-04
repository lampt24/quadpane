using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using QuadPane.Shell;

namespace QuadPane;

public partial class App : Application
{
    private static readonly string CrashLog = Path.Combine(Path.GetTempPath(), "QuadPane-crash.log");

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
        try { File.AppendAllText(CrashLog, $"[{DateTime.Now:O}] {ex}\n\n"); } catch { }
    }
}
