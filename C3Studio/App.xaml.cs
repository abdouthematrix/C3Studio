using System.Windows;
using C3Studio.Core.Services;
using C3Studio.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace C3Studio;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sc = new ServiceCollection();
        ConfigureServices(sc);
        Services = sc.BuildServiceProvider();

        var win = new MainWindow();
        MainWindow = win;
        win.Show();

        // Decide first view
        var settings = Services.GetRequiredService<ISettingsService>();
        var nav      = Services.GetRequiredService<INavigationService>();

        if (!string.IsNullOrEmpty(settings.ConquerPath))
            nav.GoToWorkspace();
        else
            nav.GoToSetup();
    }

    private static void ConfigureServices(IServiceCollection sc)
    {
        sc.AddSingleton<ISettingsService,   SettingsService>();
        sc.AddSingleton<IAssetFileService,  AssetFileService>();
        sc.AddSingleton<IGameDataService,   GameDataService>();
        sc.AddSingleton<INavigationService, NavigationService>();
        sc.AddSingleton<IAssetExportService, AssetExportService>();

        sc.AddTransient<SetupViewModel>();
        sc.AddTransient<WorkspaceViewModel>();
    }
}
