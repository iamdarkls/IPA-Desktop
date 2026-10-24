using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace IpaDesktop;

public static class Program
{
    internal static string SmokeDirectory;
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("This release supports Windows only.");
        using var mutex = new Mutex(true, "Local\\IPA-Desktop-0.1", out var created);
        if (!created) return 0;
        if (args.Length == 2 && args[0] == "--smoke-test")
        {
            SmokeDirectory = System.IO.Path.GetFullPath(args[1]);
            if (!System.IO.File.Exists(System.IO.Path.Combine(SmokeDirectory,"state.json"))) throw new ArgumentException("A smoke fixture directory is required.");
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Program.SmokeDirectory == null) desktop.MainWindow = new Desktop().Window;
            else
            {
                var engine = new Engine(Program.SmokeDirectory);
                var view = new Desktop(engine, false);
                desktop.MainWindow = view.Window;
                view.Window.Opened += async delegate { await PublishedSmoke.Run(view, engine, Program.SmokeDirectory); };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
