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

        var serviceCollection = new ServiceCollection();

        // Register Services
        serviceCollection.AddSingleton<SettingsService>();
        serviceCollection.AddSingleton<DependencyService>();
        serviceCollection.AddSingleton<YtDlpService>();
        serviceCollection.AddSingleton<FFmpegService>();

        // Register ViewModels
        serviceCollection.AddSingleton<MainViewModel>();
        serviceCollection.AddSingleton<DownloaderViewModel>();
        serviceCollection.AddSingleton<ConverterViewModel>();
        serviceCollection.AddSingleton<TrimmerViewModel>();
        serviceCollection.AddSingleton<SettingsViewModel>();

        // Register MainWindow
        serviceCollection.AddSingleton<MainWindow>();

        Services = serviceCollection.BuildServiceProvider();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
