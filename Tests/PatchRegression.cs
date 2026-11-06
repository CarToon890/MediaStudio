using System.IO;
using MediaStudio.Models;
using MediaStudio.Services;
using MediaStudio.ViewModels;

internal static class PatchRegression
{
    public static async Task RunAsync(string root, string source, FFmpegService realFfmpeg, SettingsService settings, DependencyService dependency)
    {
        var workspace = new MediaWorkspace(Path.Combine(root, "patch-history.json"));
        var ffmpeg = new ControlledFfmpeg(dependency);
        var video = new TrimmerViewModel(ffmpeg, settings, dependency, workspace);
        Check(!video.HasLoadedFile && video.StartTimeFormatted == "--:--:--" && !video.SeekToStartCommand.CanExecute(null), "Unloaded video state");
        ffmpeg.Duration = TimeSpan.Zero;
        await video.LoadFileAsync(source);
        Check(!video.HasLoadedFile && video.TotalSeconds == 0 && !video.StartTrimCommand.CanExecute(null) && video.StatusText.Contains("ไม่สำเร็จ"), "Failed duration must not invent readiness");
        var broken = Path.Combine(root, "broken-video.mp4");
        File.WriteAllText(broken, "invalid media");
        var realVideo = new TrimmerViewModel(realFfmpeg, settings, dependency, workspace);
        await realVideo.LoadFileAsync(broken);
        Check(!realVideo.HasLoadedFile && !realVideo.IsLoading, "Real corrupt media must stay unready");
        ffmpeg.Duration = TimeSpan.FromSeconds(5);
        await video.LoadFileAsync(source);
        Check(video.HasLoadedFile && video.StartTrimCommand.CanExecute(null), "Video can recover after bad input");

        var audio = new AudioTrimmerViewModel(ffmpeg, settings, dependency, workspace);
        var first = new TaskCompletionSource<AudioWaveformResult>();
        var second = new TaskCompletionSource<AudioWaveformResult>();
        var sourceB = Path.Combine(root, "second.wav");
        File.WriteAllText(sourceB, "fixture");
        ffmpeg.Analyze = (path, ct) => path == source ? first.Task : second.Task;
        var loadA = audio.LoadFileAsync(source);
        var loadB = audio.LoadFileAsync(sourceB);
        var stalePreview = Path.Combine(root, "stale-preview.mp3");
        File.WriteAllText(stalePreview, "fixture");
        first.SetResult(Analysis(7, stalePreview));
        await loadA;
        Check(audio.IsBusy && audio.SourceFilePath == sourceB && audio.WaveformPeaks.Count == 0 && !File.Exists(stalePreview), "Old audio completion must not clear new busy state or publish data");
        second.SetResult(Analysis(9));
        await loadB;
        Check(!audio.IsBusy && audio.CanTrim && audio.TotalSeconds == 9 && audio.EndSeconds == 9 && audio.VolumePercent == 100, "Latest audio load wins");
        // Opposite completion order: latest finishes first, cancelled predecessor ignores cancellation.
        first = new(); second = new();
        loadA = audio.LoadFileAsync(source); loadB = audio.LoadFileAsync(sourceB);
        second.SetResult(Analysis(11)); await loadB;
        first.SetResult(Analysis(22)); await loadA;
        Check(audio.SourceFilePath == sourceB && audio.TotalSeconds == 11 && !audio.IsBusy, "Late old result must not overwrite completed latest load");
        ffmpeg.Analyze = (_, _) => Task.FromException<AudioWaveformResult>(new OperationCanceledException());
        await audio.LoadFileAsync(source);
        Check(!audio.IsBusy && !audio.CanTrim, "Cancelled audio load resets state");
        ffmpeg.Analyze = (_, _) => Task.FromResult(Analysis(5));
        await audio.LoadFileAsync(source);

        var originalOutput = settings.Settings.DownloadDirectory;
        var blockedOutput = Path.Combine(root, "output-is-file");
        File.WriteAllText(blockedOutput, "not a folder");
        settings.Settings.DownloadDirectory = blockedOutput;
        await video.StartTrimCommand.ExecuteAsync(null);
        await audio.StartTrimCommand.ExecuteAsync(null);
        Check(!video.IsTrimming && !audio.IsBusy && video.StatusText.Contains("ไม่สำเร็จ") && audio.StatusText.Contains("ไม่สำเร็จ"), "Output directory errors must release busy state");
        settings.Settings.DownloadDirectory = originalOutput;
        ffmpeg.Trim = () => throw new OperationCanceledException();
        await video.StartTrimCommand.ExecuteAsync(null); await audio.StartTrimCommand.ExecuteAsync(null);
        Check(!video.IsTrimming && !audio.IsBusy && video.StatusText.Contains("ยกเลิก") && audio.StatusText.Contains("ยกเลิก"), "Trim cancellation must release busy state");
        ffmpeg.Trim = () => Task.FromResult(MediaOperationResult.Completed(source));
        await video.StartTrimCommand.ExecuteAsync(null); await audio.StartTrimCommand.ExecuteAsync(null);
        Check(video.LastTrimmedPath == source && audio.LastOutputPath == source, "Trim retry succeeds after errors and cancellation");

        var queue = new ConverterViewModel(realFfmpeg, settings, dependency, workspace);
        var notifications = 0;
        queue.StartConversionCommand.CanExecuteChanged += (_, _) => notifications++;
        Check(!queue.StartConversionCommand.CanExecute(null), "Empty queue disabled");
        var item = new ConversionItem { SourceFilePath = source, Status = TaskState.Completed };
        queue.ConversionList.Add(item);
        Check(!queue.StartConversionCommand.CanExecute(null), "Completed-only queue disabled");
        item.Status = TaskState.Queued;
        Check(queue.StartConversionCommand.CanExecute(null), "Queued item enables start");
        queue.IsConverting = true;
        Check(!queue.StartConversionCommand.CanExecute(null), "Running batch disables start");
        queue.IsConverting = false;
        queue.ConversionList.Remove(item);
        var before = notifications;
        item.Status = TaskState.Failed;
        Check(!queue.StartConversionCommand.CanExecute(null) && notifications == before, "Removed item unsubscribed");
        queue.ConversionList.Add(item); queue.ConversionList.Clear();
        before = notifications; item.Status = TaskState.Completed;
        Check(notifications == before, "Clear unsubscribes items");

        var downloader = new DownloaderViewModel(new YtDlpService(dependency), settings, dependency, workspace);
        var focus = false;
        downloader.RequestUrlFocus += () => focus = true;
        downloader.UrlInput = "   ";
        await downloader.FetchInfoCommand.ExecuteAsync(null);
        Check(focus && downloader.HasUrlError && !downloader.IsLoadingInfo, "Blank URL has inline feedback and focus request");
        downloader.UrlInput = "https://example.test/video";
        Check(!downloader.HasUrlError, "Editing URL clears feedback");

        var corruptHistory = Path.Combine(root, "corrupt-history.json");
        File.WriteAllText(corruptHistory, "{bad json");
        var corrupt = new MediaWorkspace(corruptHistory);
        corrupt.Register(source, "test");
        Check(corrupt.HasPersistenceWarning && File.ReadAllText(corruptHistory) == "{bad json" && corrupt.Assets.Count == 1, "Unreadable history preserved while session continues");
        Check(corrupt.PersistenceWarning.Contains("รูปแบบไม่ถูกต้อง") && !corrupt.PersistenceWarning.Contains("System."), "Corrupt history feedback must be understandable without internal type names");
        var savePath = Path.Combine(root, "save-error.json");
        var failing = new MediaWorkspace(savePath);
        failing.Register(source, "test");
        var savedBytes = File.ReadAllBytes(savePath);
        using (var locked = new FileStream(savePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var asset = failing.Register(sourceB, "test");
            Check(failing.HasPersistenceWarning && asset != null && File.Exists(asset.FilePath), "Save failure must retain usable result");
            Check(File.ReadAllBytes(savePath).SequenceEqual(savedBytes), "Failed save preserves previous history");
        }
        Check(!Directory.EnumerateFiles(root, "workspace-*.partial").Any(), "Failed save cleans staging file");
        failing.Register(sourceB, "test");
        Check(!failing.HasPersistenceWarning && new MediaWorkspace(savePath).Assets.Count == 2, "Persistence retry recovers");
        // Successful processing stays successful even when history cannot be saved.
        var failingVideo = new TrimmerViewModel(ffmpeg, settings, dependency, corrupt);
        await failingVideo.LoadFileAsync(source); await failingVideo.StartTrimCommand.ExecuteAsync(null);
        var failingAudio = new AudioTrimmerViewModel(ffmpeg, settings, dependency, corrupt);
        await failingAudio.LoadFileAsync(source); await failingAudio.StartTrimCommand.ExecuteAsync(null);
        Check(failingVideo.LastTrimmedPath == source && failingAudio.LastOutputPath == source && corrupt.HasPersistenceWarning, "History warning must not invalidate successful trims");
        Console.WriteLine("PASS: v1.1.1 readiness, recovery, audio load ordering, queue commands, URL feedback and history preservation");
    }

    private static AudioWaveformResult Analysis(double seconds, string preview = "") => new()
    { Success = true, Duration = TimeSpan.FromSeconds(seconds), Peaks = new[] { .2, .4 }, PreviewPath = preview };
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class ControlledFfmpeg(DependencyService dependency) : FFmpegService(dependency)
    {
        public TimeSpan Duration { get; set; }
        public Func<string, CancellationToken, Task<AudioWaveformResult>> Analyze { get; set; } = (_, _) => Task.FromResult(Analysis(5));
        public Func<Task<MediaOperationResult>> Trim { get; set; } = () => Task.FromResult(MediaOperationResult.Failed("fixture"));
        public override Task<TimeSpan> GetDurationAsync(string path, CancellationToken ct = default) => Task.FromResult(Duration);
        public override Task<AudioWaveformResult> AnalyzeAudioAsync(string path, int peakCount = 900, CancellationToken ct = default) => Analyze(path, ct);
        public override Task<MediaOperationResult> LosslessTrimAsync(string path, TimeSpan start, TimeSpan end, string output, CancellationToken ct = default) => Trim();
        public override Task<MediaOperationResult> TrimAudioAsync(string path, TimeSpan start, TimeSpan end, string output, string format, double volumePercent = 100, CancellationToken ct = default) => Trim();
    }
}
