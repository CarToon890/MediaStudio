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

    private long _urlRevision;
    private string _fetchedUrl = string.Empty;
    private CancellationTokenSource? _infoCts;
    private string _urlInput = string.Empty;
    public string UrlInput
    {
        get => _urlInput;
        set
        {
            if (!SetProperty(ref _urlInput, value)) return;
            _urlRevision++;
            _infoCts?.Cancel();
            CurrentMediaInfo = null;
            _fetchedUrl = string.Empty;
            IsLoadingInfo = false;
            OutputFileName = string.Empty;
            FileNameError = UrlError = string.Empty;
            StatusText = "วางลิงก์แล้วกดดึงข้อมูลเพื่อตรวจชื่อและคุณภาพ";
            StartDownloadCommand?.NotifyCanExecuteChanged();
        }
    }
    private string _urlError = string.Empty;
    public string UrlError { get => _urlError; private set { if (SetProperty(ref _urlError, value)) OnPropertyChanged(nameof(HasUrlError)); } }
    public bool HasUrlError => !string.IsNullOrEmpty(UrlError);
    public event Action? RequestUrlFocus;

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
                OnPropertyChanged(nameof(HasThumbnail));
                OnPropertyChanged(nameof(HasAuthor));
                StartDownloadCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(CurrentMediaInfo?.ThumbnailUrl);
    public bool HasAuthor => !string.IsNullOrWhiteSpace(CurrentMediaInfo?.Author);
    public bool HasMediaInfo => CurrentMediaInfo != null && _fetchedUrl == UrlInput.Trim();

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
    public string FileNameError { get => _fileNameError; set { if (SetProperty(ref _fileNameError, value)) { OnPropertyChanged(nameof(HasFileNameError)); StartDownloadCommand?.NotifyCanExecuteChanged(); } } }
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
    public IRelayCommand<DownloadItem> OpenRetainedSourceCommand { get; }
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
        FetchInfoCommand = new AsyncRelayCommand(FetchInfoAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        StartDownloadCommand = new AsyncRelayCommand(StartDownloadAsync, () => HasMediaInfo && FileNameService.TryNormalizeBaseName(OutputFileName, out _, out _));
        OpenOutputFolderCommand = new RelayCommand<DownloadItem>(OpenOutputFolder);
        PlayItemCommand = new RelayCommand<DownloadItem>(PlayItem);
        OpenRetainedSourceCommand = new RelayCommand<DownloadItem>(item =>
        {
            var error = ResultFileService.Open(item?.RetainedSourcePath ?? string.Empty, false);
            if (!string.IsNullOrEmpty(error)) StatusText = error;
        });
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
        if (!_dependencyService.Operations.TryBeginWork(out var operation)) { StatusText = "กำลังอัปเดตโปรแกรมหรือส่วนประกอบ กรุณารอสักครู่"; return; }
        using var operationLease = operation;

        if (string.IsNullOrWhiteSpace(UrlInput))
        {
            UrlError = "กรุณาวางลิงก์วิดีโอก่อนดึงข้อมูล";
            RequestUrlFocus?.Invoke();
            return;
        }
        var url = UrlInput.Trim();
        if (!YtDlpService.IsWebUrl(url)) { UrlError = "กรุณาใช้ลิงก์ HTTP หรือ HTTPS ที่ถูกต้อง"; RequestUrlFocus?.Invoke(); return; }
        var revision = ++_urlRevision;
        _infoCts?.Cancel();
        using var request = new CancellationTokenSource();
        _infoCts = request;
        UrlError = string.Empty;
        CurrentMediaInfo = null;
        OutputFileName = string.Empty;
        IsLoadingInfo = true;
        StatusText = "กำลังดึงข้อมูลวิดีโอ...";
        try
        {
            if (!_dependencyService.IsReady && !await _dependencyService.EnsureDependenciesAsync(ct: request.Token))
                throw new IOException("เตรียมระบบดาวน์โหลดไม่ได้ กรุณาลองใหม่");
            request.Token.ThrowIfCancellationRequested();
            var info = await _ytDlpService.GetMediaInfoAsync(url, request.Token);
            if (revision != _urlRevision || request.IsCancellationRequested) return;
            if (info != null)
            {
                _fetchedUrl = url;
                CurrentMediaInfo = info;
                OutputFileName = FileNameService.SanitizeSuggestedName(info.Title);
                StatusText = "ตรวจชื่อไฟล์ คุณภาพ และโฟลเดอร์ปลายทาง แล้วเริ่มดาวน์โหลด";
                StartDownloadCommand.NotifyCanExecuteChanged();
            }
            else StatusText = "ดึงข้อมูลไม่ได้ ตรวจสอบลิงก์และอินเทอร์เน็ตแล้วลองใหม่";
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (revision == _urlRevision) StatusText = "ดึงข้อมูลไม่ได้ ตรวจสอบลิงก์และอินเทอร์เน็ตแล้วลองใหม่";
        }
        finally
        {
            if (ReferenceEquals(_infoCts, request)) { _infoCts = null; IsLoadingInfo = false; }
        }
    }

    private async Task StartDownloadAsync()
    {
        if (!_dependencyService.Operations.TryBeginWork(out var operation)) { StatusText = "กำลังอัปเดตโปรแกรมหรือส่วนประกอบ กรุณารอสักครู่"; return; }
        using var operationLease = operation;

        if (!HasMediaInfo) return;
        if (!FileNameService.TryNormalizeBaseName(OutputFileName, out var normalizedName, out var fileNameError))
        {
            FileNameError = fileNameError;
            return;
        }

        var outputFolder = _settingsService.Settings.DownloadDirectory;
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
            AvailableResolutions = CurrentMediaInfo?.AvailableVideoResolutions.ToArray() ?? Array.Empty<VideoDimensions>(),
            UseHardwareAcceleration = _settingsService.Settings.EnableHardwareAcceleration,
            DurationSeconds = mediaDurationSeconds,
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
        try
        {
            Directory.CreateDirectory(outputFolder);
            var result = await _ytDlpService.DownloadAsync(downloadItem, outputFolder, cts.Token);
            if (result.Success) _workspace.Register(result.OutputPath, "Downloader", mediaDurationSeconds);
        }
        catch (OperationCanceledException) { downloadItem.Status = TaskState.Cancelled; downloadItem.StatusMessage = "ยกเลิกแล้ว"; }
        catch { downloadItem.Status = TaskState.Failed; downloadItem.StatusMessage = "ดาวน์โหลดไม่ได้ ตรวจสอบโฟลเดอร์บันทึกและลองใหม่"; }
        finally { downloadItem.Cts = null; cts.Dispose(); OnPropertyChanged(nameof(ActiveDownloadsCount)); }
    }

    private void PlayItem(DownloadItem? item)
    {
        var error = ResultFileService.Open(item?.OutputPath ?? string.Empty, folder: false);
        if (!string.IsNullOrEmpty(error)) { StatusText = error; if (item != null) item.StatusMessage = error; }
    }

    private void CancelTask(DownloadItem? item)
    {
        if (item != null && item.IsRunning)
        {
            try
            {
                item.Cts?.Cancel();
                item.StatusMessage = "กำลังยกเลิกและคืนทรัพยากร...";
                OnPropertyChanged(nameof(ActiveDownloadsCount));
            }
            catch { }
        }
    }

    private void OpenOutputFolder(DownloadItem? item)
    {
        var error = ResultFileService.Open(item?.OutputPath ?? string.Empty, folder: true);
        if (!string.IsNullOrEmpty(error)) { StatusText = error; if (item != null) item.StatusMessage = error; }
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
