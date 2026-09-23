using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaStudio.Services;
using Microsoft.Win32;

namespace MediaStudio.ViewModels;

public class SettingsViewModel : ObservableObject
{
    public string AppVersion => $"MediaStudio v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0"}";
    private readonly SettingsService _settingsService;
    private readonly DependencyService _dependencyService;
    private readonly IConfirmationService _confirmationService;

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
    public string EngineStatus
    {
        get => _engineStatus;
        set => SetProperty(ref _engineStatus, value);
    }
    public string EngineActionLabel => _dependencyService.IsReady ? "ตรวจสอบและอัปเดต" : "ติดตั้งเอนจิน";

    private bool _isUpdatingEngine;
    public bool IsUpdatingEngine
    {
        get => _isUpdatingEngine;
        set => SetProperty(ref _isUpdatingEngine, value);
    }

    public IRelayCommand BrowseDownloadFolderCommand { get; }
    public IRelayCommand OpenDownloadFolderCommand { get; }
    public IAsyncRelayCommand UpdateEnginesCommand { get; }

    public SettingsViewModel(SettingsService settingsService, DependencyService dependencyService,
        IConfirmationService confirmationService)
    {
        _settingsService = settingsService;
        _dependencyService = dependencyService;
        _confirmationService = confirmationService;

        _downloadPath = _settingsService.Settings.DownloadDirectory;
        _enableHardwareAcceleration = _settingsService.Settings.EnableHardwareAcceleration;

        BrowseDownloadFolderCommand = new RelayCommand(BrowseDownloadFolder);
        OpenDownloadFolderCommand = new RelayCommand(OpenDownloadFolder);
        UpdateEnginesCommand = new AsyncRelayCommand(UpdateEnginesAsync);

        UpdateEngineStatus();
        _dependencyService.AvailabilityChanged += UpdateEngineStatus;
    }

    private void UpdateEngineStatus()
    {
        if (_dependencyService.IsReady)
        {
            EngineStatus = "เอนจิน yt-dlp และ FFmpeg พร้อมทำงานเต็มรูปแบบ";
        }
        else
        {
            EngineStatus = "ยังไม่ได้ดาวน์โหลดเอนจินประมวลผล";
        }
        OnPropertyChanged(nameof(EngineActionLabel));
    }

    private void BrowseDownloadFolder()
    {
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = DownloadPath
        };

        if (dialog.ShowDialog() == true)
        {
            DownloadPath = dialog.FolderName;
        }
    }

    private void OpenDownloadFolder()
    {
        try
        {
            if (Directory.Exists(DownloadPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = DownloadPath,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private async Task UpdateEnginesAsync()
    {
        var refresh = _dependencyService.IsReady;
        if (!_confirmationService.ConfirmEngineDownload(refresh)) return;
        IsUpdatingEngine = true;
        EngineStatus = refresh ? "กำลังดาวน์โหลดและตรวจสอบเอนจินรุ่นล่าสุด..." : "กำลังติดตั้งเอนจิน...";

        var progress = new Progress<string>(msg => EngineStatus = msg);
        var succeeded = await _dependencyService.EnsureDependenciesAsync(progress,
            mode: refresh ? EngineSetupMode.RefreshAll : EngineSetupMode.InstallMissing);

        IsUpdatingEngine = false;
        if (succeeded) UpdateEngineStatus();
        else EngineStatus = _dependencyService.IsReady
            ? "อัปเดตไม่สำเร็จ แต่ยังใช้เอนจินเดิมได้"
            : "ติดตั้งไม่สำเร็จ ตรวจสอบอินเทอร์เน็ตแล้วลองอีกครั้ง";
    }
}
