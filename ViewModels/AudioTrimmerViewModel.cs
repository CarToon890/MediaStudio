using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaStudio.Models;
using MediaStudio.Services;
using Microsoft.Win32;

namespace MediaStudio.ViewModels;

public sealed class AudioTrimmerViewModel : ObservableObject
{
    private readonly FFmpegService _ffmpeg;
    private readonly SettingsService _settings;
    private readonly DependencyService _dependencies;
    private readonly MediaWorkspace _workspace;
    private CancellationTokenSource? _loadCts;

    private string _sourceFilePath = string.Empty;
    public string SourceFilePath { get => _sourceFilePath; set { if (SetProperty(ref _sourceFilePath, value)) OnPropertyChanged(nameof(HasLoadedFile)); } }
    public bool HasLoadedFile => File.Exists(SourceFilePath);
    private string _fileName = "ยังไม่ได้เลือกไฟล์";
    public string FileName { get => _fileName; set => SetProperty(ref _fileName, value); }
    private string _previewFilePath = string.Empty;
    public string PreviewFilePath { get => _previewFilePath; set => SetProperty(ref _previewFilePath, value); }
    private double _totalSeconds = 1;
    public double TotalSeconds { get => _totalSeconds; set => SetProperty(ref _totalSeconds, value); }
    private double _startSeconds;
    public double StartSeconds { get => _startSeconds; set { if (SetProperty(ref _startSeconds, Math.Clamp(value, 0, TotalSeconds))) RaiseTimeProperties(); } }
    private double _endSeconds = 1;
    public double EndSeconds { get => _endSeconds; set { if (SetProperty(ref _endSeconds, Math.Clamp(value, 0, TotalSeconds))) RaiseTimeProperties(); } }
    private double _currentPositionSeconds;
    public double CurrentPositionSeconds { get => _currentPositionSeconds; set { if (SetProperty(ref _currentPositionSeconds, value)) OnPropertyChanged(nameof(CurrentTimeFormatted)); } }
    public string StartTimeFormatted => TimeSpan.FromSeconds(StartSeconds).ToString(@"hh\:mm\:ss\.fff");
    public string EndTimeFormatted => TimeSpan.FromSeconds(EndSeconds).ToString(@"hh\:mm\:ss\.fff");
    public string SelectionDurationFormatted => TimeSpan.FromSeconds(Math.Max(0, EndSeconds - StartSeconds)).ToString(@"hh\:mm\:ss\.fff");
    public string CurrentTimeFormatted => TimeSpan.FromSeconds(CurrentPositionSeconds).ToString(@"hh\:mm\:ss\.fff");
    private bool _isPlaying;
    public bool IsPlaying { get => _isPlaying; set => SetProperty(ref _isPlaying, value); }
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
    private string _statusText = "เลือกไฟล์เสียงหรือวิดีโอเพื่อเริ่มตัดเสียง";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    private string _selectedOutputFormat = "MP3";
    public string SelectedOutputFormat { get => _selectedOutputFormat; set => SetProperty(ref _selectedOutputFormat, value); }
    private string _lastOutputPath = string.Empty;
    public string LastOutputPath { get => _lastOutputPath; set { if (SetProperty(ref _lastOutputPath, value)) OnPropertyChanged(nameof(HasOutput)); } }
    public bool HasOutput => File.Exists(LastOutputPath);
    public ObservableCollection<double> WaveformPeaks { get; } = new();
    public ObservableCollection<string> OutputFormats { get; } = new() { "MP3", "WAV", "FLAC" };

    public event Action<TimeSpan>? RequestSeek;
    public event Action? RequestTogglePlay;
    public IAsyncRelayCommand SelectFileCommand { get; }
    public IAsyncRelayCommand StartTrimCommand { get; }
    public IRelayCommand TogglePlayCommand { get; }
    public IRelayCommand SetStartToCurrentCommand { get; }
    public IRelayCommand SetEndToCurrentCommand { get; }
    public IRelayCommand SeekToStartCommand { get; }
    public IRelayCommand SeekToEndCommand { get; }
    public IRelayCommand OpenOutputCommand { get; }
    public IRelayCommand OpenOutputFolderCommand { get; }
    public IRelayCommand SendToConverterCommand { get; }

    public AudioTrimmerViewModel(FFmpegService ffmpeg, SettingsService settings, DependencyService dependencies, MediaWorkspace workspace)
    {
        _ffmpeg = ffmpeg;
        _settings = settings;
        _dependencies = dependencies;
        _workspace = workspace;
        CleanupOldPreviews();
        SelectFileCommand = new AsyncRelayCommand(SelectFileAsync);
        StartTrimCommand = new AsyncRelayCommand(StartTrimAsync);
        TogglePlayCommand = new RelayCommand(() => RequestTogglePlay?.Invoke());
        SetStartToCurrentCommand = new RelayCommand(() => StartSeconds = CurrentPositionSeconds);
        SetEndToCurrentCommand = new RelayCommand(() => EndSeconds = Math.Max(StartSeconds + .01, CurrentPositionSeconds));
        SeekToStartCommand = new RelayCommand(() => RequestSeek?.Invoke(TimeSpan.FromSeconds(StartSeconds)));
        SeekToEndCommand = new RelayCommand(() => RequestSeek?.Invoke(TimeSpan.FromSeconds(EndSeconds)));
        OpenOutputCommand = new RelayCommand(OpenOutput);
        OpenOutputFolderCommand = new RelayCommand(OpenOutputFolder);
        SendToConverterCommand = new RelayCommand(() => Send(ToolDestination.Converter));
    }

    private async Task SelectFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "ไฟล์เสียงและวิดีโอ|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.opus;*.mp4;*.mkv;*.mov;*.avi;*.webm|ไฟล์ทั้งหมด|*.*"
        };
        if (dialog.ShowDialog() == true) await LoadFileAsync(dialog.FileName);
    }

    public async Task LoadFileAsync(string path)
    {
        if (!File.Exists(path)) return;
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        SourceFilePath = path;
        if (File.Exists(PreviewFilePath)) File.Delete(PreviewFilePath);
        PreviewFilePath = string.Empty;
        FileName = Path.GetFileName(path);
        IsBusy = true;
        StatusText = "กำลังอ่านแทร็กเสียงและสร้างคลื่นเสียง...";
        WaveformPeaks.Clear();
        LastOutputPath = string.Empty;
        if (!_dependencies.IsReady && !await _dependencies.EnsureDependenciesAsync(ct: _loadCts.Token))
        {
            IsBusy = false;
            StatusText = "ไม่สามารถเตรียม FFmpeg ได้";
            return;
        }
        try
        {
            var analysis = await _ffmpeg.AnalyzeAudioAsync(path, ct: _loadCts.Token);
            if (!analysis.Success)
            {
                StatusText = analysis.ErrorMessage;
                return;
            }
            foreach (var peak in analysis.Peaks) WaveformPeaks.Add(peak);
            PreviewFilePath = analysis.PreviewPath;
            TotalSeconds = Math.Max(.01, analysis.Duration.TotalSeconds);
            StartSeconds = 0;
            EndSeconds = TotalSeconds;
            CurrentPositionSeconds = 0;
            var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            SelectedOutputFormat = OutputFormats.Contains(ext) ? ext : "MP3";
            _workspace.Register(path, "ไฟล์นำเข้า", TotalSeconds);
            StatusText = $"พร้อมตัดเสียง ความยาว {TimeSpan.FromSeconds(TotalSeconds).ToString(@"hh\:mm\:ss")}";
            RequestSeek?.Invoke(TimeSpan.Zero);
        }
        catch (OperationCanceledException) { }
        finally { IsBusy = false; }
    }

    private async Task StartTrimAsync()
    {
        if (!HasLoadedFile || IsBusy) return;
        if (EndSeconds <= StartSeconds || EndSeconds > TotalSeconds + .01)
        {
            StatusText = "กรุณาเลือกช่วงเวลาให้ถูกต้อง";
            return;
        }
        IsBusy = true;
        StatusText = "กำลังตัดและบันทึกเสียง...";
        var output = _settings.Settings.DownloadDirectory;
        Directory.CreateDirectory(output);
        var result = await _ffmpeg.TrimAudioAsync(SourceFilePath, TimeSpan.FromSeconds(StartSeconds), TimeSpan.FromSeconds(EndSeconds), output, SelectedOutputFormat);
        IsBusy = false;
        if (!result.Success)
        {
            StatusText = $"ตัดเสียงไม่สำเร็จ: {result.ErrorMessage}";
            return;
        }
        LastOutputPath = result.OutputPath;
        _workspace.Register(result.OutputPath, "Audio Trimmer", EndSeconds - StartSeconds);
        StatusText = $"บันทึกเสียงแล้ว: {Path.GetFileName(result.OutputPath)}";
    }

    private void RaiseTimeProperties()
    {
        OnPropertyChanged(nameof(StartTimeFormatted));
        OnPropertyChanged(nameof(EndTimeFormatted));
        OnPropertyChanged(nameof(SelectionDurationFormatted));
    }

    private void OpenOutput()
    {
        if (File.Exists(LastOutputPath)) Process.Start(new ProcessStartInfo(LastOutputPath) { UseShellExecute = true });
    }

    private void OpenOutputFolder()
    {
        if (File.Exists(LastOutputPath)) Process.Start("explorer.exe", $"/select,\"{LastOutputPath}\"");
    }

    private void Send(ToolDestination destination)
    {
        if (!File.Exists(LastOutputPath)) return;
        var asset = _workspace.Register(LastOutputPath, "Audio Trimmer");
        _workspace.RequestTransfer(asset, destination);
    }

    private static void CleanupOldPreviews()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path.GetTempPath(), "mediastudio-preview-*.mp3"))
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1)) File.Delete(file);
        }
        catch { }
    }
}
