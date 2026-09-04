using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaStudio.Services;

namespace MediaStudio.ViewModels;

public class MainViewModel : ObservableObject
{
    public DownloaderViewModel DownloaderVm { get; }
    public ConverterViewModel ConverterVm { get; }
    public TrimmerViewModel TrimmerVm { get; }
    public SettingsViewModel SettingsVm { get; }
    private readonly DependencyService _dependencyService;

    private string _currentTab = "Downloader";
    public string CurrentTab { get => _currentTab; set => SetProperty(ref _currentTab, value); }

    public string DownloaderNavTitle => DownloaderVm.ActiveDownloadsCount > 0 
        ? $"Downloader ({DownloaderVm.ActiveDownloadsCount})" 
        : "Downloader";

    public string ConverterNavTitle => ConverterVm.ActiveConversionsCount > 0 
        ? $"Converter ({ConverterVm.ActiveConversionsCount})" 
        : "Converter";

    private string _appStatus = "MediaStudio พร้อมใช้งาน";
    public string AppStatus { get => _appStatus; set => SetProperty(ref _appStatus, value); }

    public IRelayCommand<string> NavigateCommand { get; }

    public MainViewModel(
        DownloaderViewModel downloaderVm,
        ConverterViewModel converterVm,
        TrimmerViewModel trimmerVm,
        SettingsViewModel settingsVm,
        DependencyService dependencyService)
    {
        DownloaderVm = downloaderVm;
        ConverterVm = converterVm;
        TrimmerVm = trimmerVm;
        SettingsVm = settingsVm;
        _dependencyService = dependencyService;

        DownloaderVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DownloaderViewModel.ActiveDownloadsCount))
            {
                OnPropertyChanged(nameof(DownloaderNavTitle));
            }
        };

        ConverterVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ConverterViewModel.ActiveConversionsCount))
            {
                OnPropertyChanged(nameof(ConverterNavTitle));
            }
        };

        NavigateCommand = new RelayCommand<string>(Navigate);

        _ = InitializeEnginesAsync();
    }

    private async Task InitializeEnginesAsync()
    {
        if (!_dependencyService.IsReady)
        {
            AppStatus = "กำลังตรวจสอบเอนจินประมวลผล (yt-dlp & FFmpeg)...";
            await _dependencyService.EnsureDependenciesAsync();
            AppStatus = "เอนจินพร้อมใช้งานเรียบร้อยแล้ว";
        }
    }

    private void Navigate(string? tabName)
    {
        if (!string.IsNullOrEmpty(tabName))
        {
            CurrentTab = tabName;
        }
    }
}
