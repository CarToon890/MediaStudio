using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaStudio.Models;
using MediaStudio.Services;

namespace MediaStudio.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public HomeViewModel HomeVm { get; }
    public DownloaderViewModel DownloaderVm { get; }
    public ConverterViewModel ConverterVm { get; }
    public TrimmerViewModel TrimmerVm { get; }
    public AudioTrimmerViewModel AudioTrimmerVm { get; }
    public WorkspaceViewModel WorkspaceVm { get; }
    public SettingsViewModel SettingsVm { get; }
    private readonly DependencyService _dependencies;
    private readonly SettingsService _settings;

    private string _currentTab;
    public string CurrentTab { get => _currentTab; set => SetProperty(ref _currentTab, value); }
    private string _appStatus = "กำลังเตรียม MediaStudio...";
    public string AppStatus { get => _appStatus; set => SetProperty(ref _appStatus, value); }
    private bool _enginesReady;
    public bool EnginesReady { get => _enginesReady; set => SetProperty(ref _enginesReady, value); }
    private bool _isEngineInitializing;
    public bool IsEngineInitializing { get => _isEngineInitializing; set => SetProperty(ref _isEngineInitializing, value); }
    private double _engineProgress;
    public double EngineProgress { get => _engineProgress; set => SetProperty(ref _engineProgress, value); }
    public string DownloaderNavTitle => DownloaderVm.ActiveDownloadsCount > 0 ? $"ดาวน์โหลด ({DownloaderVm.ActiveDownloadsCount})" : "ดาวน์โหลด";
    public string ConverterNavTitle => ConverterVm.ActiveConversionsCount > 0 ? $"แปลงไฟล์ ({ConverterVm.ActiveConversionsCount})" : "แปลงไฟล์";
    public IRelayCommand<string> NavigateCommand { get; }
    public IAsyncRelayCommand RetryEnginesCommand { get; }

    public MainViewModel(HomeViewModel homeVm, DownloaderViewModel downloaderVm, ConverterViewModel converterVm,
        TrimmerViewModel trimmerVm, AudioTrimmerViewModel audioTrimmerVm, WorkspaceViewModel workspaceVm,
        SettingsViewModel settingsVm, DependencyService dependencies, SettingsService settings, MediaWorkspace workspace)
    {
        HomeVm = homeVm;
        DownloaderVm = downloaderVm;
        ConverterVm = converterVm;
        TrimmerVm = trimmerVm;
        AudioTrimmerVm = audioTrimmerVm;
        WorkspaceVm = workspaceVm;
        SettingsVm = settingsVm;
        _dependencies = dependencies;
        _settings = settings;
        _currentTab = dependencies.IsReady && settings.Settings.HasCompletedOnboarding ? NormalizeTab(settings.Settings.LastVisitedTab) : "Home";

        NavigateCommand = new RelayCommand<string>(Navigate);
        RetryEnginesCommand = new AsyncRelayCommand(InitializeEnginesAsync);
        HomeVm.NavigationRequested += Navigate;
        workspace.TransferRequested += OnTransferRequested;
        dependencies.DownloadProgressChanged += (name, progress) =>
        {
            EngineProgress = progress;
            AppStatus = $"กำลังดาวน์โหลด {name} {progress:F0}%";
        };
        DownloaderVm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DownloaderViewModel.ActiveDownloadsCount)) OnPropertyChanged(nameof(DownloaderNavTitle)); };
        ConverterVm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ConverterViewModel.ActiveConversionsCount)) OnPropertyChanged(nameof(ConverterNavTitle)); };
        _ = InitializeEnginesAsync();
    }

    private async Task InitializeEnginesAsync()
    {
        if (IsEngineInitializing) return;
        IsEngineInitializing = true;
        EngineProgress = 0;
        AppStatus = "กำลังตรวจสอบเอนจิน yt-dlp และ FFmpeg...";
        EnginesReady = _dependencies.IsReady || await _dependencies.EnsureDependenciesAsync(new Progress<string>(message => AppStatus = message));
        AppStatus = EnginesReady ? "เอนจินพร้อมใช้งาน" : "เตรียมเอนจินไม่สำเร็จ กดลองใหม่จากหน้า Home";
        if (EnginesReady) EngineProgress = 100;
        IsEngineInitializing = false;
        if (EnginesReady && CurrentTab == "Home" && _settings.Settings.HasCompletedOnboarding && _settings.Settings.LastVisitedTab != "Home")
            Navigate(_settings.Settings.LastVisitedTab);
    }

    private void Navigate(string? tabName)
    {
        if (string.IsNullOrWhiteSpace(tabName)) return;
        if (RequiresEngines(tabName) && !EnginesReady)
        {
            CurrentTab = "Home";
            AppStatus = "กรุณารอให้เอนจินพร้อมก่อนเปิดเครื่องมือ";
            return;
        }
        CurrentTab = NormalizeTab(tabName);
        if (RequiresEngines(CurrentTab)) _settings.Settings.HasCompletedOnboarding = true;
        _settings.Settings.LastVisitedTab = CurrentTab;
        _settings.SaveSettings();
    }

    private async void OnTransferRequested(MediaAsset asset, ToolDestination destination)
    {
        if (!EnginesReady)
        {
            Navigate("Home");
            return;
        }
        switch (destination)
        {
            case ToolDestination.Converter:
                ConverterVm.AddFiles(new[] { asset.FilePath });
                Navigate("Converter");
                break;
            case ToolDestination.VideoTrimmer when asset.Kind == MediaKind.Video:
                Navigate("Trimmer");
                await TrimmerVm.LoadFileAsync(asset.FilePath);
                break;
            case ToolDestination.AudioTrimmer when asset.Kind is MediaKind.Video or MediaKind.Audio:
                Navigate("AudioTrimmer");
                await AudioTrimmerVm.LoadFileAsync(asset.FilePath);
                break;
        }
    }

    private static bool RequiresEngines(string tab) => tab is "Downloader" or "Converter" or "Trimmer" or "AudioTrimmer";
    private static string NormalizeTab(string? tab) => tab is "Home" or "Downloader" or "Converter" or "Trimmer" or "AudioTrimmer" or "Workspace" or "Settings" ? tab : "Home";
}
