using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaStudio.Models;
using MediaStudio.Services;
using Microsoft.Win32;

namespace MediaStudio.ViewModels;

public class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly DependencyService _dependencyService;
    private readonly IConfirmationService _confirmationService;
    private readonly IAppUpdateService _appUpdateService;
    private readonly DownloaderViewModel _downloader;
    private readonly ConverterViewModel _converter;
    private readonly TrimmerViewModel _trimmer;
    private readonly AudioTrimmerViewModel _audioTrimmer;
    private readonly Version _currentVersion = AppUpdateService.NormalizeVersion(
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 1, 0));
    private AppReleaseInfo? _latestRelease;

    public string AppVersion => $"MediaStudio v{_currentVersion.ToString(3)}";
    public string CurrentVersionDisplay => $"เวอร์ชันปัจจุบัน {_currentVersion.ToString(3)}";

    private string _downloadPath = string.Empty;
    public string DownloadPath
    {
        get => _downloadPath;
        set
        {
            if (SetProperty(ref _downloadPath, value))
            {
                _settingsService.Settings.DownloadDirectory = value;
                _settingsService.SaveSettings();
            }
        }
    }

    private bool _enableHardwareAcceleration;
    public bool EnableHardwareAcceleration
    {
        get => _enableHardwareAcceleration;
        set
        {
            if (SetProperty(ref _enableHardwareAcceleration, value))
            {
                _settingsService.Settings.EnableHardwareAcceleration = value;
                _settingsService.SaveSettings();
            }
        }
    }

    private string _engineStatus = "กำลังตรวจสอบ...";
    public string EngineStatus { get => _engineStatus; set => SetProperty(ref _engineStatus, value); }
    public string EngineActionLabel => _dependencyService.IsReady
        ? "ดาวน์โหลดส่วนประกอบใหม่"
        : "ดาวน์โหลดส่วนประกอบที่จำเป็น";

    private bool _isUpdatingEngine;
    public bool IsUpdatingEngine { get => _isUpdatingEngine; set => SetProperty(ref _isUpdatingEngine, value); }

    private string _appUpdateStatus = "ยังไม่ได้ตรวจสอบเวอร์ชันล่าสุด";
    public string AppUpdateStatus { get => _appUpdateStatus; set => SetProperty(ref _appUpdateStatus, value); }

    private double _appUpdateProgress;
    public double AppUpdateProgress { get => _appUpdateProgress; set => SetProperty(ref _appUpdateProgress, value); }

    private bool _isAppUpdateBusy;
    public bool IsAppUpdateBusy
    {
        get => _isAppUpdateBusy;
        set
        {
            if (!SetProperty(ref _isAppUpdateBusy, value)) return;
            OnPropertyChanged(nameof(AppUpdateButtonLabel));
        }
    }

    private bool _isAppUpdateAvailable;
    public bool IsAppUpdateAvailable { get => _isAppUpdateAvailable; set => SetProperty(ref _isAppUpdateAvailable, value); }

    private bool _canAutoUpdate;
    public bool CanAutoUpdate { get => _canAutoUpdate; set => SetProperty(ref _canAutoUpdate, value); }
    public string AppUpdateButtonLabel => IsAppUpdateBusy ? "กำลังตรวจสอบ..." :
        _latestRelease is null ? "ตรวจสอบเวอร์ชันใหม่" : "ตรวจสอบอีกครั้ง";

    public IRelayCommand BrowseDownloadFolderCommand { get; }
    public IRelayCommand OpenDownloadFolderCommand { get; }
    public IAsyncRelayCommand UpdateEnginesCommand { get; }
    public IAsyncRelayCommand CheckAppUpdateCommand { get; }
    public IAsyncRelayCommand ApplyAppUpdateCommand { get; }
    public IRelayCommand OpenLatestReleaseCommand { get; }

    public SettingsViewModel(SettingsService settingsService, DependencyService dependencyService,
        IConfirmationService confirmationService, IAppUpdateService appUpdateService,
        DownloaderViewModel downloader, ConverterViewModel converter, TrimmerViewModel trimmer,
        AudioTrimmerViewModel audioTrimmer)
    {
        _settingsService = settingsService;
        _dependencyService = dependencyService;
        _confirmationService = confirmationService;
        _appUpdateService = appUpdateService;
        _downloader = downloader;
        _converter = converter;
        _trimmer = trimmer;
        _audioTrimmer = audioTrimmer;
        _downloadPath = _settingsService.Settings.DownloadDirectory;
        _enableHardwareAcceleration = _settingsService.Settings.EnableHardwareAcceleration;

        BrowseDownloadFolderCommand = new RelayCommand(BrowseDownloadFolder);
        OpenDownloadFolderCommand = new RelayCommand(OpenDownloadFolder);
        UpdateEnginesCommand = new AsyncRelayCommand(UpdateEnginesAsync);
        CheckAppUpdateCommand = new AsyncRelayCommand(CheckAppUpdateAsync);
        ApplyAppUpdateCommand = new AsyncRelayCommand(ApplyAppUpdateAsync);
        OpenLatestReleaseCommand = new RelayCommand(OpenLatestRelease);

        UpdateEngineStatus();
        _dependencyService.AvailabilityChanged += UpdateEngineStatus;
    }

    private void UpdateEngineStatus()
    {
        EngineStatus = _dependencyService.IsReady
            ? "ระบบดาวน์โหลดมีเดียและระบบแปลง/บีบอัดไฟล์พร้อมใช้งานแล้ว"
            : "ยังไม่ได้ดาวน์โหลดส่วนประกอบที่จำเป็น";
        OnPropertyChanged(nameof(EngineActionLabel));
    }

    private void BrowseDownloadFolder()
    {
        var dialog = new OpenFolderDialog { InitialDirectory = DownloadPath };
        if (dialog.ShowDialog() == true) DownloadPath = dialog.FolderName;
    }

    private void OpenDownloadFolder()
    {
        try
        {
            if (Directory.Exists(DownloadPath))
                Process.Start(new ProcessStartInfo { FileName = DownloadPath, UseShellExecute = true });
        }
        catch { }
    }

    private async Task UpdateEnginesAsync()
    {
        if (IsAppUpdateBusy) return;
        var refresh = _dependencyService.IsReady;
        if (!_confirmationService.ConfirmEngineDownload(refresh)) return;
        IsUpdatingEngine = true;
        EngineStatus = refresh ? "กำลังดาวน์โหลดส่วนประกอบใหม่..." : "กำลังดาวน์โหลดส่วนประกอบที่จำเป็น...";
        try
        {
            var progress = new Progress<string>(message => EngineStatus = MakeEngineMessageFriendly(message));
            var succeeded = await _dependencyService.EnsureDependenciesAsync(progress,
                mode: refresh ? EngineSetupMode.RefreshAll : EngineSetupMode.InstallMissing);
            if (succeeded) UpdateEngineStatus();
            else EngineStatus = _dependencyService.IsReady
                ? "ดาวน์โหลดไม่สำเร็จ แต่ระบบเดิมยังใช้งานได้"
                : "ติดตั้งไม่สำเร็จ ตรวจสอบอินเทอร์เน็ตแล้วลองอีกครั้ง";
        }
        finally { IsUpdatingEngine = false; }
    }

    private async Task CheckAppUpdateAsync()
    {
        if (IsAppUpdateBusy) return;
        IsAppUpdateBusy = true;
        IsAppUpdateAvailable = false;
        CanAutoUpdate = false;
        AppUpdateProgress = 0;
        AppUpdateStatus = "กำลังตรวจสอบ stable release ล่าสุด...";
        try
        {
            var result = await _appUpdateService.CheckLatestAsync(_currentVersion);
            if (result.Status == AppUpdateCheckStatus.Failed || result.Release is null)
            {
                _latestRelease = null;
                AppUpdateStatus = result.ErrorMessage;
                return;
            }

            _latestRelease = result.Release;
            IsAppUpdateAvailable = result.IsUpdateAvailable;
            CanAutoUpdate = result.IsUpdateAvailable && result.Release.CanAutoUpdate;
            if (!result.IsUpdateAvailable)
                AppUpdateStatus = "คุณใช้ MediaStudio เวอร์ชันล่าสุดแล้ว";
            else if (result.Release.CanAutoUpdate)
                AppUpdateStatus = $"พบ MediaStudio {result.Release.TagName} พร้อมให้อัปเดต";
            else
                AppUpdateStatus = $"พบ MediaStudio {result.Release.TagName} แต่ไม่มีแพ็กเกจอัปเดตอัตโนมัติ กรุณาเปิดหน้า Release";
        }
        finally
        {
            IsAppUpdateBusy = false;
            OnPropertyChanged(nameof(AppUpdateButtonLabel));
        }
    }

    private async Task ApplyAppUpdateAsync()
    {
        if (IsAppUpdateBusy || _latestRelease is null || !CanAutoUpdate) return;
        if (HasBlockingWork())
        {
            AppUpdateStatus = "กรุณารอให้งานดาวน์โหลดหรือประมวลผลเสร็จก่อนอัปเดตโปรแกรม";
            return;
        }
        if (!_appUpdateService.CanWriteToApplicationDirectory(out var writeError))
        {
            AppUpdateStatus = writeError;
            return;
        }
        if (!_confirmationService.ConfirmApplicationUpdate(_currentVersion.ToString(3),
                _latestRelease.Version.ToString(3))) return;

        IsAppUpdateBusy = true;
        AppUpdateProgress = 0;
        try
        {
            var progress = new Progress<AppUpdateProgress>(value =>
            {
                AppUpdateStatus = value.Message;
                AppUpdateProgress = value.Percentage;
            });
            var staged = await _appUpdateService.StageUpdateAsync(_latestRelease, progress);
            AppUpdateStatus = "กำลังปิดโปรแกรมเพื่อติดตั้งเวอร์ชันใหม่...";
            var launch = _appUpdateService.LaunchUpdater(staged);
            if (!launch.Success)
            {
                AppUpdateStatus = launch.ErrorMessage;
                return;
            }
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            AppUpdateStatus = "อัปเดตไม่สำเร็จ โปรแกรมเดิมยังใช้งานได้: " + ex.Message;
        }
        finally { IsAppUpdateBusy = false; }
    }

    private void OpenLatestRelease()
    {
        if (_latestRelease is null) return;
        try { _appUpdateService.OpenReleasePage(_latestRelease.ReleaseUrl); }
        catch { AppUpdateStatus = "ไม่สามารถเปิดหน้า Release ได้"; }
    }

    private bool HasBlockingWork() => _downloader.IsLoadingInfo || _downloader.ActiveDownloadsCount > 0 ||
        _converter.IsConverting || _converter.ActiveConversionsCount > 0 || _trimmer.IsTrimming ||
        _audioTrimmer.IsBusy || IsUpdatingEngine || _dependencyService.IsBusy;

    internal static string MakeEngineMessageFriendly(string message)
    {
        return message
            .Replace("yt-dlp", "ส่วนดาวน์โหลดมีเดีย", StringComparison.OrdinalIgnoreCase)
            .Replace("FFmpeg", "ส่วนแปลงและบีบอัดไฟล์", StringComparison.OrdinalIgnoreCase)
            .Replace("เอนจิน", "ส่วนประกอบ", StringComparison.Ordinal);
    }
}
