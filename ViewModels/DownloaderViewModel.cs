using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaStudio.Models;
using MediaStudio.Services;

namespace MediaStudio.ViewModels;

public class DownloaderViewModel : ObservableObject
{
    private readonly YtDlpService _ytDlpService;
    private readonly SettingsService _settingsService;
    private readonly DependencyService _dependencyService;
    private readonly MediaWorkspace _workspace;

    private string _urlInput = string.Empty;
    public string UrlInput { get => _urlInput; set => SetProperty(ref _urlInput, value); }

    private bool _isLoadingInfo;
    public bool IsLoadingInfo { get => _isLoadingInfo; set => SetProperty(ref _isLoadingInfo, value); }

    private MediaMetadata? _currentMediaInfo;
    public MediaMetadata? CurrentMediaInfo
    {
        get => _currentMediaInfo;
        set
        {
            if (SetProperty(ref _currentMediaInfo, value))
            {
                OnPropertyChanged(nameof(HasMediaInfo));
            }
        }
    }

    public bool HasMediaInfo => CurrentMediaInfo != null;

    private string _selectedQuality = "1080p";
    public string SelectedQuality { get => _selectedQuality; set => SetProperty(ref _selectedQuality, value); }

    private bool _isAudioOnly;
    public bool IsAudioOnly
    {
        get => _isAudioOnly;
        set
        {
            if (SetProperty(ref _isAudioOnly, value)) OnPropertyChanged(nameof(OutputExtension));
        }
    }

    private string _outputFileName = string.Empty;
    public string OutputFileName
    {
        get => _outputFileName;
        set
        {
            if (SetProperty(ref _outputFileName, value))
            {
                FileNameError = FileNameService.TryNormalizeBaseName(value, out _, out var error) ? string.Empty : error;
            }
        }
    }

    private string _fileNameError = string.Empty;
    public string FileNameError { get => _fileNameError; set { if (SetProperty(ref _fileNameError, value)) OnPropertyChanged(nameof(HasFileNameError)); } }
    public bool HasFileNameError => !string.IsNullOrEmpty(FileNameError);
    public string OutputExtension => IsAudioOnly ? ".mp3" : ".mp4";

    private string _statusText = "วางลิงก์วิดีโอเพื่อเริ่มต้นดาวน์โหลด";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public ObservableCollection<string> QualityOptions { get; } = new()
    {
        "4K (2160p)",
        "2K (1440p)",
        "1080p",
        "720p",
        "480p"
    };

    public ObservableCollection<DownloadItem> DownloadQueue { get; } = new();

    public int ActiveDownloadsCount => DownloadQueue.Count(i => i.IsRunning);
    public bool HasDownloadItems => DownloadQueue.Count > 0;

    public IAsyncRelayCommand PasteClipboardCommand { get; }
    public IAsyncRelayCommand FetchInfoCommand { get; }
    public IAsyncRelayCommand StartDownloadCommand { get; }
    public IRelayCommand<DownloadItem> OpenOutputFolderCommand { get; }
    public IRelayCommand<DownloadItem> PlayItemCommand { get; }
    public IRelayCommand<DownloadItem> CancelTaskCommand { get; }
    public IRelayCommand<DownloadItem> RemoveItemCommand { get; }
    public IRelayCommand<DownloadItem> SendToConverterCommand { get; }
    public IRelayCommand<DownloadItem> SendToVideoTrimmerCommand { get; }
    public IRelayCommand<DownloadItem> SendToAudioTrimmerCommand { get; }

    public DownloaderViewModel(YtDlpService ytDlpService, SettingsService settingsService, DependencyService dependencyService, MediaWorkspace workspace)
    {
        _ytDlpService = ytDlpService;
        _settingsService = settingsService;
        _dependencyService = dependencyService;
        _workspace = workspace;

        PasteClipboardCommand = new AsyncRelayCommand(PasteClipboardAsync);
        FetchInfoCommand = new AsyncRelayCommand(FetchInfoAsync);
        StartDownloadCommand = new AsyncRelayCommand(StartDownloadAsync);
        OpenOutputFolderCommand = new RelayCommand<DownloadItem>(OpenOutputFolder);
        PlayItemCommand = new RelayCommand<DownloadItem>(PlayItem);
        CancelTaskCommand = new RelayCommand<DownloadItem>(CancelTask);
        RemoveItemCommand = new RelayCommand<DownloadItem>(RemoveItem);
        SendToConverterCommand = new RelayCommand<DownloadItem>(item => Send(item, ToolDestination.Converter));
        SendToVideoTrimmerCommand = new RelayCommand<DownloadItem>(item => Send(item, ToolDestination.VideoTrimmer));
        SendToAudioTrimmerCommand = new RelayCommand<DownloadItem>(item => Send(item, ToolDestination.AudioTrimmer));

        DownloadQueue.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(ActiveDownloadsCount));
            OnPropertyChanged(nameof(HasDownloadItems));
        };
    }

    private async Task PasteClipboardAsync()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText().Trim();
                if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                    text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    UrlInput = text;
                    await FetchInfoAsync();
                }
            }
        }
        catch { }
    }

    private async Task FetchInfoAsync()
    {
        if (string.IsNullOrWhiteSpace(UrlInput)) return;

        CurrentMediaInfo = null;
        OutputFileName = string.Empty;
        IsLoadingInfo = true;
        StatusText = "กำลังดึงข้อมูลวิดีโอ...";

        try
        {
            if (!_dependencyService.IsReady)
            {
                StatusText = "กำลังตรวจสอบเอนจิน...";
                await _dependencyService.EnsureDependenciesAsync();
            }

            var info = await _ytDlpService.GetMediaInfoAsync(UrlInput);
            if (info != null)
            {
                CurrentMediaInfo = info;
                OutputFileName = FileNameService.SanitizeSuggestedName(info.Title);
                StatusText = $"พบข้อมูล: {info.Title}";
            }
            else
            {
                StatusText = "ไม่สามารถดึงข้อมูลได้ โปรดตรวจสอบลิงก์อีกครั้ง";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"เกิดข้อผิดพลาด: {ex.Message}";
        }
        finally
        {
            IsLoadingInfo = false;
        }
    }

    private async Task StartDownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(UrlInput)) return;
        if (!FileNameService.TryNormalizeBaseName(OutputFileName, out var normalizedName, out var fileNameError))
        {
            FileNameError = fileNameError;
            return;
        }

        var cts = new CancellationTokenSource();
        var mediaDurationSeconds = CurrentMediaInfo?.Duration.TotalSeconds ?? 0;
        var downloadItem = new DownloadItem
        {
            Title = CurrentMediaInfo?.Title ?? "กำลังเตรียมการ...",
            Url = UrlInput,
            ThumbnailUrl = CurrentMediaInfo?.ThumbnailUrl ?? string.Empty,
            Quality = SelectedQuality,
            IsAudioOnly = IsAudioOnly,
            RequestedFileName = normalizedName,
            Status = TaskState.Queued,
            StatusMessage = "รอเริ่มดาวน์โหลด...",
            Cts = cts
        };

        downloadItem.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(DownloadItem.Status) or nameof(DownloadItem.IsRunning))
            {
                OnPropertyChanged(nameof(ActiveDownloadsCount));
            }
        };

        DownloadQueue.Insert(0, downloadItem);
        OnPropertyChanged(nameof(ActiveDownloadsCount));
        UrlInput = string.Empty;
        CurrentMediaInfo = null;
        OutputFileName = string.Empty;

        var outputFolder = _settingsService.Settings.DownloadDirectory;
        Directory.CreateDirectory(outputFolder);

        var result = await _ytDlpService.DownloadAsync(downloadItem, outputFolder, cts.Token);
        if (result.Success) _workspace.Register(result.OutputPath, "Downloader", mediaDurationSeconds);
        OnPropertyChanged(nameof(ActiveDownloadsCount));
    }

    private void PlayItem(DownloadItem? item)
    {
        try
        {
            if (item != null && !string.IsNullOrEmpty(item.OutputPath) && File.Exists(item.OutputPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.OutputPath,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private void CancelTask(DownloadItem? item)
    {
        if (item != null && item.IsRunning)
        {
            try
            {
                item.Cts?.Cancel();
                item.Status = TaskState.Cancelled;
                item.StatusMessage = "ยกเลิกโดยผู้ใช้";
                OnPropertyChanged(nameof(ActiveDownloadsCount));
            }
            catch { }
        }
    }

    private void OpenOutputFolder(DownloadItem? item)
    {
        try
        {
            if (item != null && !string.IsNullOrEmpty(item.OutputPath))
            {
                if (File.Exists(item.OutputPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{item.OutputPath}\"");
                    return;
                }
                if (Directory.Exists(item.OutputPath))
                {
                    Process.Start(new ProcessStartInfo { FileName = item.OutputPath, UseShellExecute = true });
                    return;
                }
            }

            var defaultFolder = _settingsService.Settings.DownloadDirectory;
            if (Directory.Exists(defaultFolder))
            {
                Process.Start(new ProcessStartInfo { FileName = defaultFolder, UseShellExecute = true });
            }
        }
        catch { }
    }

    private void RemoveItem(DownloadItem? item)
    {
        if (item != null)
        {
            if (item.IsRunning)
            {
                CancelTask(item);
            }
            DownloadQueue.Remove(item);
            OnPropertyChanged(nameof(ActiveDownloadsCount));
        }
    }

    private void Send(DownloadItem? item, ToolDestination destination)
    {
        if (item == null || !item.IsCompleted || !File.Exists(item.OutputPath)) return;
        var asset = _workspace.Register(item.OutputPath, "Downloader");
        _workspace.RequestTransfer(asset, destination);
    }
}
