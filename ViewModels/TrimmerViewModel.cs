using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaStudio.Models;
using MediaStudio.Services;
using Microsoft.Win32;

namespace MediaStudio.ViewModels;

public class TrimmerViewModel : ObservableObject
{
    private readonly FFmpegService _ffmpegService;
    private readonly SettingsService _settingsService;
    private readonly DependencyService _dependencyService;
    private readonly MediaWorkspace _workspace;

    private string _sourceFilePath = string.Empty;
    public string SourceFilePath 
    { 
        get => _sourceFilePath; 
        set 
        { 
            if (SetProperty(ref _sourceFilePath, value))
            {
                OnPropertyChanged(nameof(HasLoadedFile));
            }
        } 
    }

    public bool HasLoadedFile => !string.IsNullOrEmpty(SourceFilePath) && File.Exists(SourceFilePath);

    private string _fileName = string.Empty;
    public string FileName { get => _fileName; set => SetProperty(ref _fileName, value); }

    private TimeSpan _totalDuration = TimeSpan.Zero;
    public TimeSpan TotalDuration { get => _totalDuration; set => SetProperty(ref _totalDuration, value); }

    private double _totalSeconds = 100;
    public double TotalSeconds { get => _totalSeconds; set => SetProperty(ref _totalSeconds, value); }

    private bool _isPlaying;
    public bool IsPlaying { get => _isPlaying; set => SetProperty(ref _isPlaying, value); }

    private double _currentPositionSeconds;
    public double CurrentPositionSeconds
    {
        get => _currentPositionSeconds;
        set
        {
            if (SetProperty(ref _currentPositionSeconds, value))
            {
                OnPropertyChanged(nameof(CurrentPositionFormatted));
            }
        }
    }

    public string CurrentPositionFormatted => TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss\.f");

    private double _startSeconds;
    public double StartSeconds
    {
        get => _startSeconds;
        set
        {
            if (SetProperty(ref _startSeconds, value))
            {
                OnPropertyChanged(nameof(StartTimeFormatted));
                OnPropertyChanged(nameof(TrimDurationFormatted));
                if (value >= EndSeconds && TotalSeconds > 0)
                {
                    EndSeconds = Math.Min(TotalSeconds, value + 1);
                }
            }
        }
    }

    private double _endSeconds = 10;
    public double EndSeconds
    {
        get => _endSeconds;
        set
        {
            if (SetProperty(ref _endSeconds, value))
            {
                OnPropertyChanged(nameof(EndTimeFormatted));
                OnPropertyChanged(nameof(TrimDurationFormatted));
            }
        }
    }

    private bool _isTrimming;
    public bool IsTrimming { get => _isTrimming; set => SetProperty(ref _isTrimming, value); }

    private string _statusText = "เลือกไฟล์วิดีโอเพื่อเริ่มต้นตัดคลิป";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private string _lastTrimmedPath = string.Empty;
    public string LastTrimmedPath
    {
        get => _lastTrimmedPath;
        set
        {
            if (SetProperty(ref _lastTrimmedPath, value))
            {
                OnPropertyChanged(nameof(HasLastTrimmedPath));
            }
        }
    }

    public bool HasLastTrimmedPath => !string.IsNullOrEmpty(LastTrimmedPath);

    public string StartTimeFormatted => TimeSpan.FromSeconds(StartSeconds).ToString(@"hh\:mm\:ss\.f");
    public string EndTimeFormatted => TimeSpan.FromSeconds(EndSeconds).ToString(@"hh\:mm\:ss\.f");
    public string TrimDurationFormatted => TimeSpan.FromSeconds(Math.Max(0, EndSeconds - StartSeconds)).ToString(@"hh\:mm\:ss\.f");

    public event Action<TimeSpan>? RequestSeek;
    public event Action? RequestTogglePlay;

    public IAsyncRelayCommand SelectFileCommand { get; }
    public IAsyncRelayCommand StartTrimCommand { get; }
    public IRelayCommand OpenLastTrimmedFolderCommand { get; }
    public IRelayCommand PlayLastTrimmedCommand { get; }
    public IRelayCommand TogglePlayCommand { get; }
    public IRelayCommand SetStartToCurrentCommand { get; }
    public IRelayCommand SetEndToCurrentCommand { get; }
    public IRelayCommand SeekToStartCommand { get; }
    public IRelayCommand SeekToEndCommand { get; }
    public IRelayCommand SendToConverterCommand { get; }
    public IRelayCommand SendToAudioTrimmerCommand { get; }

    public TrimmerViewModel(FFmpegService ffmpegService, SettingsService settingsService, DependencyService dependencyService, MediaWorkspace workspace)
    {
        _ffmpegService = ffmpegService;
        _settingsService = settingsService;
        _dependencyService = dependencyService;
        _workspace = workspace;

        SelectFileCommand = new AsyncRelayCommand(SelectFileAsync);
        StartTrimCommand = new AsyncRelayCommand(StartTrimAsync);
        OpenLastTrimmedFolderCommand = new RelayCommand(OpenLastTrimmedFolder);
        PlayLastTrimmedCommand = new RelayCommand(PlayLastTrimmed);
        TogglePlayCommand = new RelayCommand(() => RequestTogglePlay?.Invoke());
        SetStartToCurrentCommand = new RelayCommand(() => StartSeconds = CurrentPositionSeconds);
        SetEndToCurrentCommand = new RelayCommand(() => EndSeconds = Math.Max(StartSeconds + 0.1, CurrentPositionSeconds));
        SeekToStartCommand = new RelayCommand(() => RequestSeek?.Invoke(TimeSpan.FromSeconds(StartSeconds)));
        SeekToEndCommand = new RelayCommand(() => RequestSeek?.Invoke(TimeSpan.FromSeconds(EndSeconds)));
        SendToConverterCommand = new RelayCommand(() => Send(ToolDestination.Converter));
        SendToAudioTrimmerCommand = new RelayCommand(() => Send(ToolDestination.AudioTrimmer));
    }

    private async Task SelectFileAsync()
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "Video Files (*.mp4;*.mkv;*.mov;*.avi;*.webm)|*.mp4;*.mkv;*.mov;*.avi;*.webm|All Files (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            await LoadFileAsync(openFileDialog.FileName);
        }
    }

    public async Task LoadFileAsync(string filePath)
    {
        if (!File.Exists(filePath)) return;

        SourceFilePath = filePath;
        _workspace.Register(filePath, "ไฟล์นำเข้า");
        FileName = Path.GetFileName(filePath);
        StatusText = "กำลังอ่านข้อมูลความยาววิดีโอ...";

        if (!_dependencyService.IsReady)
        {
            await _dependencyService.EnsureDependenciesAsync();
        }

        TotalDuration = await _ffmpegService.GetDurationAsync(filePath);
        TotalSeconds = TotalDuration.TotalSeconds > 0 ? TotalDuration.TotalSeconds : 60;
        StartSeconds = 0;
        EndSeconds = TotalSeconds > 10 ? 10 : TotalSeconds;
        CurrentPositionSeconds = 0;

        StatusText = $"โหลดไฟล์สำเร็จ: ความยาวทั้งหมด {TotalDuration:hh\\:mm\\:ss}";
        RequestSeek?.Invoke(TimeSpan.Zero);
    }

    private async Task StartTrimAsync()
    {
        if (string.IsNullOrEmpty(SourceFilePath) || !File.Exists(SourceFilePath) || IsTrimming) return;

        if (EndSeconds <= StartSeconds)
        {
            StatusText = "เวลาสิ้นสุดต้องมากกว่าเวลาเริ่มต้น";
            return;
        }

        IsTrimming = true;
        StatusText = "กำลังตัดคลิป (โหมด Lossless ความเร็วสูง)...";

        var outputFolder = _settingsService.Settings.DownloadDirectory;
        Directory.CreateDirectory(outputFolder);

        var start = TimeSpan.FromSeconds(StartSeconds);
        var end = TimeSpan.FromSeconds(EndSeconds);

        var result = await _ffmpegService.LosslessTrimAsync(SourceFilePath, start, end, outputFolder);

        IsTrimming = false;
        if (result.Success)
        {
            StatusText = "ตัดวิดีโอเสร็จสมบูรณ์";
            LastTrimmedPath = result.OutputPath;
            _workspace.Register(result.OutputPath, "Video Trimmer", (end - start).TotalSeconds);
        }
        else
        {
            StatusText = $"ตัดวิดีโอไม่สำเร็จ: {result.ErrorMessage}";
        }
    }

    private void Send(ToolDestination destination)
    {
        if (!File.Exists(LastTrimmedPath)) return;
        var asset = _workspace.Register(LastTrimmedPath, "Video Trimmer");
        _workspace.RequestTransfer(asset, destination);
    }

    private void PlayLastTrimmed()
    {
        try
        {
            if (!string.IsNullOrEmpty(LastTrimmedPath) && File.Exists(LastTrimmedPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = LastTrimmedPath,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private void OpenLastTrimmedFolder()
    {
        try
        {
            if (!string.IsNullOrEmpty(LastTrimmedPath))
            {
                if (File.Exists(LastTrimmedPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{LastTrimmedPath}\"");
                    return;
                }
                if (Directory.Exists(LastTrimmedPath))
                {
                    Process.Start(new ProcessStartInfo { FileName = LastTrimmedPath, UseShellExecute = true });
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
