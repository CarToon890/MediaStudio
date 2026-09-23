using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaStudio.Models;

public enum TaskState
{
    Queued,
    FetchingInfo,
    Downloading,
    Converting,
    Completed,
    Failed,
    Cancelled
}

public enum MediaKind
{
    Video,
    Audio,
    Image,
    Other
}

public enum ToolDestination
{
    Converter,
    VideoTrimmer,
    AudioTrimmer
}

public sealed class MediaOperationResult
{
    public bool Success { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;

    public static MediaOperationResult Completed(string path) => new() { Success = true, OutputPath = path };
    public static MediaOperationResult Failed(string message) => new() { ErrorMessage = message };
}

public sealed class AudioWaveformResult
{
    public bool Success { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<double> Peaks { get; init; } = Array.Empty<double>();
    public string PreviewPath { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}

public sealed class MediaAsset : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public MediaKind Kind { get; set; }
    public string SourceTool { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public double DurationSeconds { get; set; }
    public string CreatedAtFormatted => CreatedAt.ToString("dd/MM/yyyy HH:mm");
    public string KindLabel => Kind switch
    {
        MediaKind.Video => "วิดีโอ",
        MediaKind.Audio => "เสียง",
        MediaKind.Image => "รูปภาพ",
        _ => "ไฟล์"
    };
    public bool CanOpenInVideoTrimmer => Kind == MediaKind.Video;
    public bool CanOpenInAudioTrimmer => Kind is MediaKind.Video or MediaKind.Audio;
}

public class MediaMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public string DurationFormatted => Duration.Hours > 0 
        ? Duration.ToString(@"hh\:mm\:ss") 
        : Duration.ToString(@"mm\:ss");
    public string Url { get; set; } = string.Empty;
    public string Extractor { get; set; } = string.Empty;
}

public class DownloadItem : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    public string Id { get => _id; set => SetProperty(ref _id, value); }

    private string _title = "กำลังดึงข้อมูล...";
    public string Title { get => _title; set => SetProperty(ref _title, value); }

    private string _url = string.Empty;
    public string Url { get => _url; set => SetProperty(ref _url, value); }

    private string _thumbnailUrl = string.Empty;
    public string ThumbnailUrl { get => _thumbnailUrl; set => SetProperty(ref _thumbnailUrl, value); }

    private string _quality = "1080p";
    public string Quality { get => _quality; set => SetProperty(ref _quality, value); }

    private bool _isAudioOnly;
    public bool IsAudioOnly
    {
        get => _isAudioOnly;
        set
        {
            if (SetProperty(ref _isAudioOnly, value)) OnPropertyChanged(nameof(CanSendToVideoTrimmer));
        }
    }

    private double _progress;
    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }

    private string _speed = "-- MB/s";
    public string Speed { get => _speed; set => SetProperty(ref _speed, value); }

    private string _eta = "--:--";
    public string Eta { get => _eta; set => SetProperty(ref _eta, value); }

    private TaskState _status = TaskState.Queued;
    public TaskState Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(CanSendToVideoTrimmer));
            }
        }
    }

    public bool IsRunning => Status == TaskState.Downloading || Status == TaskState.FetchingInfo;
    public bool IsCompleted => Status == TaskState.Completed;
    public bool CanSendToVideoTrimmer => IsCompleted && !IsAudioOnly;

    private string _statusMessage = "รอในคิว...";
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    private string _outputPath = string.Empty;
    public string OutputPath { get => _outputPath; set => SetProperty(ref _outputPath, value); }

    private string _requestedFileName = string.Empty;
    public string RequestedFileName { get => _requestedFileName; set => SetProperty(ref _requestedFileName, value); }

    public System.Threading.CancellationTokenSource? Cts { get; set; }
}

public class ConversionItem : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    public string Id { get => _id; set => SetProperty(ref _id, value); }

    private string _sourceFilePath = string.Empty;
    public string SourceFilePath { get => _sourceFilePath; set => SetProperty(ref _sourceFilePath, value); }

    private string _fileName = string.Empty;
    public string FileName { get => _fileName; set => SetProperty(ref _fileName, value); }

    private string _fileSizeFormatted = string.Empty;
    public string FileSizeFormatted { get => _fileSizeFormatted; set => SetProperty(ref _fileSizeFormatted, value); }

    private string _targetFormat = "MP4";
    public string TargetFormat
    {
        get => _targetFormat;
        set
        {
            if (SetProperty(ref _targetFormat, value))
            {
                OnPropertyChanged(nameof(CanOpenOutputInVideoTrimmer));
                OnPropertyChanged(nameof(CanOpenOutputInAudioTrimmer));
            }
        }
    }
    public bool CanOpenOutputInVideoTrimmer => TargetFormat is "MP4" or "MKV";
    public bool CanOpenOutputInAudioTrimmer => TargetFormat is "MP4" or "MKV" or "MP3" or "WAV" or "FLAC";

    private bool _compressVideo;
    public bool CompressVideo { get => _compressVideo; set => SetProperty(ref _compressVideo, value); }

    private int _targetSizeMb = 25;
    public int TargetSizeMb { get => _targetSizeMb; set => SetProperty(ref _targetSizeMb, value); }

    private double _progress;
    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }

    private TaskState _status = TaskState.Queued;
    public TaskState Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsCompleted));
            }
        }
    }

    public bool IsRunning => Status == TaskState.Converting;
    public bool IsCompleted => Status == TaskState.Completed;

    private string _statusMessage = "พร้อมแปลงไฟล์";
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    private string _outputPath = string.Empty;
    public string OutputPath { get => _outputPath; set => SetProperty(ref _outputPath, value); }

    public System.Threading.CancellationTokenSource? Cts { get; set; }
}
