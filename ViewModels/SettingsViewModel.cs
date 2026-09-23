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

    private bool _isUpdatingEngine;
    public bool IsUpdatingEngine
    {
        get => _isUpdatingEngine;
        set => SetProperty(ref _isUpdatingEngine, value);
    }

    public IRelayCommand BrowseDownloadFolderCommand { get; }
    public IRelayCommand OpenDownloadFolderCommand { get; }
    public IAsyncRelayCommand UpdateEnginesCommand { get; }

    public SettingsViewModel(SettingsService settingsService, DependencyService dependencyService)
    {
        _settingsService = settingsService;
        _dependencyService = dependencyService;

        _downloadPath = _settingsService.Settings.DownloadDirectory;
        _enableHardwareAcceleration = _settingsService.Settings.EnableHardwareAcceleration;

        BrowseDownloadFolderCommand = new RelayCommand(BrowseDownloadFolder);
        OpenDownloadFolderCommand = new RelayCommand(OpenDownloadFolder);
        UpdateEnginesCommand = new AsyncRelayCommand(UpdateEnginesAsync);

        UpdateEngineStatus();
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
        IsUpdatingEngine = true;
        EngineStatus = "กำลังตรวจสอบและดาวน์โหลดเอนจินล่าสุด...";

        var progress = new Progress<string>(msg => EngineStatus = msg);
        await _dependencyService.EnsureDependenciesAsync(progress);

        IsUpdatingEngine = false;
        UpdateEngineStatus();
    }
}
