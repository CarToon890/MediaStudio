using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaStudio.Models;
using MediaStudio.Services;
using Microsoft.Win32;

namespace MediaStudio.ViewModels;

public class ConverterViewModel : ObservableObject
{
    private readonly FFmpegService _ffmpegService;
    private readonly SettingsService _settingsService;
    private readonly DependencyService _dependencyService;
    private CancellationTokenSource? _convertCts;

    private string _selectedTargetFormat = "MP4";
    public string SelectedTargetFormat
    {
        get => _selectedTargetFormat;
        set
        {
            if (SetProperty(ref _selectedTargetFormat, value))
            {
                OnPropertyChanged(nameof(CanCompressVideo));
                if (!CanCompressVideo) IsCompressEnabled = false;
            }
        }
    }

    public bool CanCompressVideo => SelectedTargetFormat is "MP4" or "MKV";

    private bool _isCompressEnabled;
    public bool IsCompressEnabled { get => _isCompressEnabled; set => SetProperty(ref _isCompressEnabled, value); }

    private int _targetSizeMb = 25;
    public int TargetSizeMb { get => _targetSizeMb; set => SetProperty(ref _targetSizeMb, value); }

    private bool _isConverting;
    public bool IsConverting { get => _isConverting; set => SetProperty(ref _isConverting, value); }

    private string _statusText = "ลากไฟล์มาวางหรือกดเลือกไฟล์เพื่อเริ่มต้นแปลงไฟล์";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public ObservableCollection<string> TargetFormats { get; } = new()
    {
        "MP4",
        "MP3",
        "MKV",
        "GIF",
        "WAV",
        "FLAC"
    };

    public ObservableCollection<ConversionItem> ConversionList { get; } = new();

    public int ActiveConversionsCount => ConversionList.Count(i => i.IsRunning);

    public IRelayCommand SelectFilesCommand { get; }
    public IAsyncRelayCommand StartConversionCommand { get; }
    public IRelayCommand<ConversionItem> RemoveItemCommand { get; }
    public IRelayCommand ClearCompletedCommand { get; }
    public IRelayCommand<ConversionItem> OpenOutputFolderCommand { get; }
    public IRelayCommand<ConversionItem> PlayItemCommand { get; }
    public IRelayCommand<ConversionItem> CancelTaskCommand { get; }
    public IRelayCommand CancelAllCommand { get; }

    public ConverterViewModel(FFmpegService ffmpegService, SettingsService settingsService, DependencyService dependencyService)
    {
        _ffmpegService = ffmpegService;
        _settingsService = settingsService;
        _dependencyService = dependencyService;

        SelectFilesCommand = new RelayCommand(SelectFiles);
        StartConversionCommand = new AsyncRelayCommand(StartConversionAsync);
        RemoveItemCommand = new RelayCommand<ConversionItem>(RemoveItem);
        ClearCompletedCommand = new RelayCommand(ClearCompleted);
        OpenOutputFolderCommand = new RelayCommand<ConversionItem>(OpenOutputFolder);
        PlayItemCommand = new RelayCommand<ConversionItem>(PlayItem);
        CancelTaskCommand = new RelayCommand<ConversionItem>(CancelTask);
        CancelAllCommand = new RelayCommand(CancelAll);

        ConversionList.CollectionChanged += (s, e) => OnPropertyChanged(nameof(ActiveConversionsCount));
    }

    private void SelectFiles()
    {
        var openFileDialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Media Files (*.mp4;*.mkv;*.mov;*.avi;*.webm;*.mp3;*.wav;*.flac)|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.mp3;*.wav;*.flac|All Files (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            AddFiles(openFileDialog.FileNames);
        }
    }

    public void AddFiles(string[] filePaths)
    {
        foreach (var path in filePaths)
        {
            if (File.Exists(path) && !ConversionList.Any(c => c.SourceFilePath == path))
            {
                var fileInfo = new FileInfo(path);
                var sizeMb = fileInfo.Length / (1024.0 * 1024.0);

                var item = new ConversionItem
                {
                    SourceFilePath = path,
                    FileName = fileInfo.Name,
                    FileSizeFormatted = sizeMb >= 1024 ? $"{sizeMb / 1024:F2} GB" : $"{sizeMb:F1} MB",
                    TargetFormat = SelectedTargetFormat,
                    CompressVideo = IsCompressEnabled,
                    TargetSizeMb = TargetSizeMb,
                    Status = TaskState.Queued,
                    StatusMessage = "พร้อมแปลงไฟล์"
                };

                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName is nameof(ConversionItem.Status) or nameof(ConversionItem.IsRunning))
                    {
                        OnPropertyChanged(nameof(ActiveConversionsCount));
                    }
                };

                ConversionList.Add(item);
            }
        }

        OnPropertyChanged(nameof(ActiveConversionsCount));
        StatusText = $"มีไฟล์ในรายการ {ConversionList.Count} ไฟล์";
    }

    private async Task StartConversionAsync()
    {
        if (ConversionList.Count == 0 || IsConverting) return;

        IsConverting = true;
        StatusText = "กำลังเริ่มกระบวนการแปลงไฟล์...";
        using var batchCts = new CancellationTokenSource();
        _convertCts = batchCts;
        var pendingItems = ConversionList.Where(i => i.Status != TaskState.Completed).ToList();
        var targetFormat = SelectedTargetFormat;
        var compressVideo = IsCompressEnabled && CanCompressVideo;
        var targetSizeMb = TargetSizeMb;

        try
        {
            if (!_dependencyService.IsReady)
            {
                StatusText = "กำลังเตรียมเอนจิน FFmpeg...";
                if (!await _dependencyService.EnsureDependenciesAsync(ct: batchCts.Token))
                {
                    batchCts.Token.ThrowIfCancellationRequested();
                    StatusText = "ไม่สามารถเตรียมเอนจินได้ กรุณาลองใหม่";
                    return;
                }
            }

            batchCts.Token.ThrowIfCancellationRequested();
            var outputFolder = _settingsService.Settings.DownloadDirectory;
            Directory.CreateDirectory(outputFolder);
            var useNvenc = _settingsService.Settings.EnableHardwareAcceleration;

            foreach (var item in pendingItems)
            {
                batchCts.Token.ThrowIfCancellationRequested();
                // Removed items must not run; newly added items wait for the next batch.
                if (!ConversionList.Contains(item)) continue;

                item.TargetFormat = targetFormat;
                item.CompressVideo = compressVideo;
                item.TargetSizeMb = targetSizeMb;

                using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(batchCts.Token);
                item.Cts = itemCts;
                try
                {
                    await _ffmpegService.ConvertAsync(item, outputFolder, useNvenc, itemCts.Token);
                }
                finally
                {
                    item.Cts = null;
                }
            }

            batchCts.Token.ThrowIfCancellationRequested();
            StatusText = ConversionList.Any(i => i.Status == TaskState.Queued)
                ? "แปลงไฟล์ชุดนี้เสร็จแล้ว มีไฟล์ใหม่รอเริ่มแปลง"
                : "ดำเนินการแปลงไฟล์ในคิวเรียบร้อยแล้ว";
        }
        catch (OperationCanceledException)
        {
            StatusText = "ยกเลิกการแปลงไฟล์ทั้งหมดแล้ว";
        }
        catch (Exception ex)
        {
            StatusText = $"เกิดข้อผิดพลาด: {ex.Message}";
        }
        finally
        {
            _convertCts = null;
            IsConverting = false;
            OnPropertyChanged(nameof(ActiveConversionsCount));
        }
    }

    private void PlayItem(ConversionItem? item)
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

    private void CancelTask(ConversionItem? item)
    {
        if (item != null && item.IsRunning)
        {
            try
            {
                item.Cts?.Cancel();
                item.Status = TaskState.Cancelled;
                item.StatusMessage = "ยกเลิกโดยผู้ใช้";
                OnPropertyChanged(nameof(ActiveConversionsCount));
            }
            catch { }
        }
    }

    private void CancelAll()
    {
        _convertCts?.Cancel();
        foreach (var item in ConversionList.Where(c => c.IsRunning))
        {
            item.Cts?.Cancel();
            item.Status = TaskState.Cancelled;
            item.StatusMessage = "ยกเลิกโดยผู้ใช้";
        }
        StatusText = "ยกเลิกการแปลงไฟล์ทั้งหมดแล้ว";
        OnPropertyChanged(nameof(ActiveConversionsCount));
    }

    private void RemoveItem(ConversionItem? item)
    {
        if (item != null)
        {
            if (item.IsRunning)
            {
                CancelTask(item);
            }
            ConversionList.Remove(item);
            OnPropertyChanged(nameof(ActiveConversionsCount));
        }
    }

    private void ClearCompleted()
    {
        var completed = ConversionList.Where(c => c.Status == TaskState.Completed).ToList();
        foreach (var item in completed) ConversionList.Remove(item);
        OnPropertyChanged(nameof(ActiveConversionsCount));
    }

    private void OpenOutputFolder(ConversionItem? item)
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
}
