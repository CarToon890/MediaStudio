using System;
using System.Windows;
using MediaStudio.Services;
using MediaStudio.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MediaStudio;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (UpdateBootstrapper.IsUpdateMode(e.Args))
        {
            Shutdown(UpdateBootstrapper.ApplyUpdate(e.Args));
            return;
        }

        var serviceCollection = new ServiceCollection();

        // Register Services
        serviceCollection.AddSingleton<SettingsService>();
        serviceCollection.AddSingleton<DependencyService>();
        serviceCollection.AddSingleton<YtDlpService>();
        serviceCollection.AddSingleton<FFmpegService>();
        serviceCollection.AddSingleton<MediaWorkspace>();
        serviceCollection.AddSingleton<IConfirmationService, ConfirmationService>();
        serviceCollection.AddSingleton<IAppUpdateService, AppUpdateService>();

        // Register ViewModels
        serviceCollection.AddSingleton<MainViewModel>();
        serviceCollection.AddSingleton<DownloaderViewModel>();
        serviceCollection.AddSingleton<ConverterViewModel>();
        serviceCollection.AddSingleton<TrimmerViewModel>();
        serviceCollection.AddSingleton<SettingsViewModel>();
        serviceCollection.AddSingleton<AudioTrimmerViewModel>();
        serviceCollection.AddSingleton<WorkspaceViewModel>();
        serviceCollection.AddSingleton<HomeViewModel>();

        // Register MainWindow
        serviceCollection.AddSingleton<MainWindow>();

        Services = serviceCollection.BuildServiceProvider();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        UpdateBootstrapper.CompleteNormalStartup(e.Args);
    }
}
