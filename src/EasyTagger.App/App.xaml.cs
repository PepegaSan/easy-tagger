using System.Windows;
using System.Windows.Interop;
using EasyTagger.Core;

namespace EasyTagger.App;

public partial class App : Application
{
    public static TagConfig Config { get; private set; } = null!;
    public static string ConfigPath { get; private set; } = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var startInTray = e.Args.Any(arg => string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase));
        if (!SingleInstance.TryOwn(showExisting: !startInTray))
        {
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EasyTagger",
            "config.json");
        Config = ConfigStore.LoadOrCreate(ConfigPath);
        UiText.UseGerman(Config.Language != "en");
        if (AutostartService.IsEnabled)
            AutostartService.RefreshPath();
        var window = new MainWindow();
        MainWindow = window;
        _ = new WindowInteropHelper(window).EnsureHandle();
        if (!startInTray)
            window.Show();
    }
}
