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
    private string _inputContext = "เลือกไฟล์จากเครื่อง";
    public string InputContext { get => _inputContext; set => SetProperty(ref _inputContext, value); }

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

    private bool _hasAnalyzedFile;
    public bool HasLoadedFile => _hasAnalyzedFile && File.Exists(SourceFilePath);
    public bool CanEdit => HasLoadedFile && !IsTrimming && !IsLoading;
    private bool _isLoading;
    public bool IsLoading { get => _isLoading; private set { if (SetProperty(ref _isLoading, value)) RefreshReadiness(); } }
    private CancellationTokenSource? _loadCts;

    private void RefreshReadiness()
    {
        OnPropertyChanged(nameof(HasLoadedFile));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(StartTimeFormatted));
        OnPropertyChanged(nameof(EndTimeFormatted));
        OnPropertyChanged(nameof(TrimDurationFormatted));
        OnPropertyChanged(nameof(CurrentPositionFormatted));
        StartTrimCommand?.NotifyCanExecuteChanged();
        TogglePlayCommand?.NotifyCanExecuteChanged();
        SetStartToCurrentCommand?.NotifyCanExecuteChanged();
        SetEndToCurrentCommand?.NotifyCanExecuteChanged();
        SeekToStartCommand?.NotifyCanExecuteChanged();
        SeekToEndCommand?.NotifyCanExecuteChanged();
        SelectFileCommand?.NotifyCanExecuteChanged();
    }

    private string _fileName = "ยังไม่ได้เลือกไฟล์วิดีโอ";
    public string FileName { get => _fileName; set => SetProperty(ref _fileName, value); }

    private TimeSpan _totalDuration = TimeSpan.Zero;
    public TimeSpan TotalDuration { get => _totalDuration; set => SetProperty(ref _totalDuration, value); }

    private double _totalSeconds;
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

    public string CurrentPositionFormatted => !HasLoadedFile ? "--:--:--" : TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss\.f");

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

    private double _endSeconds;
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
    public bool IsTrimming { get => _isTrimming; set { if (SetProperty(ref _isTrimming, value)) RefreshReadiness(); } }

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

    public string StartTimeFormatted => !HasLoadedFile ? "--:--:--" : TimeSpan.FromSeconds(StartSeconds).ToString(@"hh\:mm\:ss\.f");
    public string EndTimeFormatted => !HasLoadedFile ? "--:--:--" : TimeSpan.FromSeconds(EndSeconds).ToString(@"hh\:mm\:ss\.f");
    public string TrimDurationFormatted => !HasLoadedFile ? "--:--:--" : TimeSpan.FromSeconds(Math.Max(0, EndSeconds - StartSeconds)).ToString(@"hh\:mm\:ss\.f");

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

        SelectFileCommand = new AsyncRelayCommand(SelectFileAsync, () => !IsTrimming);
        StartTrimCommand = new AsyncRelayCommand(StartTrimAsync, () => CanEdit);
        OpenLastTrimmedFolderCommand = new RelayCommand(OpenLastTrimmedFolder);
        PlayLastTrimmedCommand = new RelayCommand(PlayLastTrimmed);
        TogglePlayCommand = new RelayCommand(() => RequestTogglePlay?.Invoke(), () => CanEdit);
        SetStartToCurrentCommand = new RelayCommand(() => StartSeconds = CurrentPositionSeconds, () => CanEdit);
        SetEndToCurrentCommand = new RelayCommand(() => EndSeconds = Math.Max(StartSeconds + 0.1, CurrentPositionSeconds), () => CanEdit);
        SeekToStartCommand = new RelayCommand(() => RequestSeek?.Invoke(TimeSpan.FromSeconds(StartSeconds)), () => CanEdit);
        SeekToEndCommand = new RelayCommand(() => RequestSeek?.Invoke(TimeSpan.FromSeconds(EndSeconds)), () => CanEdit);
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
            InputContext = "เลือกไฟล์จากเครื่อง";
            await LoadFileAsync(openFileDialog.FileName);
        }
    }

    public async Task LoadFileAsync(string filePath)
    {
        if (IsTrimming) return;
        _loadCts?.Cancel();
        using var load = new CancellationTokenSource();
        _loadCts = load;
        _hasAnalyzedFile = false;
        IsPlaying = false;
        SourceFilePath = string.Empty;
        TotalDuration = TimeSpan.Zero;
        TotalSeconds = StartSeconds = EndSeconds = CurrentPositionSeconds = 0;
        LastTrimmedPath = string.Empty;
        FileName = Path.GetFileName(filePath);
        IsLoading = true;
        RefreshReadiness();
        StatusText = "กำลังอ่านข้อมูลความยาววิดีโอ...";
        try
        {
            if (!File.Exists(filePath)) throw new IOException("ไม่พบไฟล์ต้นทาง");
            if (!_dependencyService.IsReady && !await _dependencyService.EnsureDependenciesAsync(ct: load.Token))
                throw new IOException("ไม่สามารถเตรียม FFmpeg ได้");
            load.Token.ThrowIfCancellationRequested();
            var duration = await _ffmpegService.GetDurationAsync(filePath, load.Token);
            load.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_loadCts, load)) return;
            if (duration.TotalSeconds <= 0) throw new IOException("ไม่สามารถอ่านความยาววิดีโอได้ ไฟล์อาจเสียหายหรือไม่รองรับ");
            TotalDuration = duration;
            TotalSeconds = duration.TotalSeconds;
            EndSeconds = Math.Min(10, TotalSeconds);
            SourceFilePath = filePath;
            _hasAnalyzedFile = true;
            RefreshReadiness();
            _workspace.Register(filePath, "ไฟล์นำเข้า");
            StatusText = $"โหลดไฟล์สำเร็จ: ความยาวทั้งหมด {duration:hh\\:mm\\:ss}";
            RequestSeek?.Invoke(TimeSpan.Zero);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_loadCts, load)) StatusText = $"อ่านวิดีโอไม่สำเร็จ: {ex.Message} กรุณาเลือกไฟล์ใหม่";
        }
        finally
        {
            if (ReferenceEquals(_loadCts, load)) { _loadCts = null; IsLoading = false; RefreshReadiness(); }
        }
    }

    private async Task StartTrimAsync()
    {
        if (!CanEdit) return;

        if (StartSeconds < 0 || EndSeconds <= StartSeconds || EndSeconds > TotalSeconds)
        {
            StatusText = "เวลาสิ้นสุดต้องมากกว่าเวลาเริ่มต้น";
            return;
        }

        IsTrimming = true;
        StatusText = "กำลังตัดคลิป (โหมด Lossless ความเร็วสูง)...";

        try
        {
            var outputFolder = _settingsService.Settings.DownloadDirectory;
            Directory.CreateDirectory(outputFolder);

            var start = TimeSpan.FromSeconds(StartSeconds);
            var end = TimeSpan.FromSeconds(EndSeconds);

            var result = await _ffmpegService.LosslessTrimAsync(SourceFilePath, start, end, outputFolder);

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
        catch (OperationCanceledException) { StatusText = "ยกเลิกการตัดวิดีโอแล้ว สามารถลองใหม่ได้"; }
        catch (Exception ex) { StatusText = $"ตัดวิดีโอไม่สำเร็จ: {ex.Message} ตรวจสอบโฟลเดอร์บันทึกแล้วลองใหม่"; }
        finally { IsTrimming = false; }
    }

    private void Send(ToolDestination destination)
    {
        if (!File.Exists(LastTrimmedPath)) return;
        var asset = _workspace.Register(LastTrimmedPath, "Video Trimmer");
        _workspace.RequestTransfer(asset, destination);
    }

    private void PlayLastTrimmed()
    {
        var error = ResultFileService.Open(LastTrimmedPath, folder: false);
        if (!string.IsNullOrEmpty(error)) StatusText = error;
    }

    private void OpenLastTrimmedFolder()
    {
        var error = ResultFileService.Open(LastTrimmedPath, folder: true);
        if (!string.IsNullOrEmpty(error)) StatusText = error;
    }
}
