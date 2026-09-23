using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaStudio.Models;
using MediaStudio.Services;

namespace MediaStudio.ViewModels;

public enum EngineUiState
{
    NotInstalled,
    Downloading,
    Ready,
    Failed,
    Cancelled
}

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
    private readonly IConfirmationService _confirmation;

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
    private EngineUiState _engineState;
    public EngineUiState EngineState
    {
        get => _engineState;
        set
        {
            if (!SetProperty(ref _engineState, value)) return;
            OnPropertyChanged(nameof(EngineActionLabel));
            OnPropertyChanged(nameof(EngineStatusTitle));
        }
    }
    public string EngineActionLabel => EnginesReady ? "ตรวจสอบและอัปเดต" : "ติดตั้งเอนจิน";
    public string EngineToolTip => EnginesReady ? string.Empty : "ติดตั้ง yt-dlp และ FFmpeg จากหน้าเริ่มต้นก่อนใช้งาน";
    public string EngineStatusTitle => EngineState switch
    {
        EngineUiState.Downloading => "กำลังเตรียมเอนจิน",
        EngineUiState.Ready => "พร้อมใช้งาน",
        EngineUiState.Failed => "เตรียมเอนจินไม่สำเร็จ",
        EngineUiState.Cancelled => "ยังไม่ได้ติดตั้งเอนจิน",
        _ => "ต้องติดตั้งเอนจินก่อนเริ่มใช้งาน"
    };
    public string DownloaderNavTitle => DownloaderVm.ActiveDownloadsCount > 0 ? $"ดาวน์โหลด ({DownloaderVm.ActiveDownloadsCount})" : "ดาวน์โหลด";
    public string ConverterNavTitle => ConverterVm.ActiveConversionsCount > 0 ? $"แปลงไฟล์ ({ConverterVm.ActiveConversionsCount})" : "แปลงไฟล์";
    public IRelayCommand<string> NavigateCommand { get; }
    public IAsyncRelayCommand SetupEnginesCommand { get; }

    public MainViewModel(HomeViewModel homeVm, DownloaderViewModel downloaderVm, ConverterViewModel converterVm,
        TrimmerViewModel trimmerVm, AudioTrimmerViewModel audioTrimmerVm, WorkspaceViewModel workspaceVm,
        SettingsViewModel settingsVm, DependencyService dependencies, SettingsService settings, MediaWorkspace workspace,
        IConfirmationService confirmation)
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
        _confirmation = confirmation;
        _currentTab = dependencies.IsReady && settings.Settings.HasCompletedOnboarding ? NormalizeTab(settings.Settings.LastVisitedTab) : "Home";
        _enginesReady = dependencies.IsReady;
        _engineState = dependencies.IsReady ? EngineUiState.Ready : EngineUiState.NotInstalled;
        _appStatus = dependencies.IsReady ? "เอนจินพร้อมใช้งาน" : "ยังไม่ได้ติดตั้ง yt-dlp และ FFmpeg";

        NavigateCommand = new RelayCommand<string>(Navigate);
        SetupEnginesCommand = new AsyncRelayCommand(ConfirmAndSetupEnginesAsync);
        HomeVm.NavigationRequested += Navigate;
        workspace.TransferRequested += OnTransferRequested;
        dependencies.DownloadProgressChanged += (name, progress) =>
        {
            EngineProgress = progress;
            AppStatus = $"กำลังดาวน์โหลด {name} {progress:F0}%";
        };
        dependencies.AvailabilityChanged += RefreshEngineAvailability;
        DownloaderVm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DownloaderViewModel.ActiveDownloadsCount)) OnPropertyChanged(nameof(DownloaderNavTitle)); };
        ConverterVm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ConverterViewModel.ActiveConversionsCount)) OnPropertyChanged(nameof(ConverterNavTitle)); };
    }

    public async Task InitializeOnStartupAsync()
    {
        if (_dependencies.IsReady)
        {
            SetReadyState();
            if (_settings.Settings.HasCompletedOnboarding && _settings.Settings.LastVisitedTab != "Home")
                Navigate(_settings.Settings.LastVisitedTab);
            return;
        }

        SetUnavailableState("ยังไม่ได้ติดตั้ง yt-dlp และ FFmpeg");
        if (_settings.Settings.HasSeenEnginePrompt) return;
        _settings.Settings.HasSeenEnginePrompt = true;
        _settings.SaveSettings();
        if (_confirmation.ConfirmEngineDownload(false))
            await InitializeEnginesAsync(EngineSetupMode.InstallMissing);
        else
        {
            EngineState = EngineUiState.Cancelled;
            AppStatus = "เลือกติดตั้งภายหลังได้จากหน้าเริ่มต้น";
        }
    }

    private async Task ConfirmAndSetupEnginesAsync()
    {
        if (IsEngineInitializing) return;
        var refresh = _dependencies.IsReady;
        if (!_confirmation.ConfirmEngineDownload(refresh)) return;
        await InitializeEnginesAsync(refresh ? EngineSetupMode.RefreshAll : EngineSetupMode.InstallMissing);
    }

    private async Task InitializeEnginesAsync(EngineSetupMode mode)
    {
        if (IsEngineInitializing) return;
        IsEngineInitializing = true;
        EngineState = EngineUiState.Downloading;
        EngineProgress = 0;
        AppStatus = mode == EngineSetupMode.RefreshAll ? "กำลังอัปเดตเอนจิน..." : "กำลังดาวน์โหลดเอนจิน...";
        try
        {
            var succeeded = await _dependencies.EnsureDependenciesAsync(
                new Progress<string>(message => AppStatus = message), mode: mode);
            EnginesReady = _dependencies.IsReady;
            if (succeeded) SetReadyState();
            else if (_dependencies.IsReady)
            {
                EngineState = EngineUiState.Failed;
                AppStatus = "อัปเดตไม่สำเร็จ แต่ยังใช้เอนจินเดิมได้";
                OnPropertyChanged(nameof(EngineActionLabel));
            }
            else
            {
                EngineState = EngineUiState.Failed;
                AppStatus = "เตรียมเอนจินไม่สำเร็จ ตรวจสอบอินเทอร์เน็ตแล้วลองอีกครั้ง";
            }
        }
        finally { IsEngineInitializing = false; }
    }

    private void RefreshEngineAvailability()
    {
        if (_dependencies.IsReady) SetReadyState();
        else SetUnavailableState("ยังไม่ได้ติดตั้ง yt-dlp และ FFmpeg");
    }

    private void SetReadyState()
    {
        EnginesReady = true;
        EngineState = EngineUiState.Ready;
        EngineProgress = 100;
        AppStatus = "เอนจิน yt-dlp และ FFmpeg พร้อมใช้งาน";
        OnPropertyChanged(nameof(EngineActionLabel));
        OnPropertyChanged(nameof(EngineToolTip));
    }

    private void SetUnavailableState(string message)
    {
        EnginesReady = false;
        EngineState = EngineUiState.NotInstalled;
        EngineProgress = 0;
        AppStatus = message;
        OnPropertyChanged(nameof(EngineActionLabel));
        OnPropertyChanged(nameof(EngineToolTip));
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
