using System.IO;
using System.Text.Json;
using MediaStudio.Models;
using MediaStudio.Services;
using MediaStudio.ViewModels;

internal static class UxRegression
{
    public static async Task RunAsync(string root, string source, FFmpegService ffmpeg, DependencyService dependency)
    {
        var data = Directory.CreateDirectory(Path.Combine(root, "ux-settings")).FullName;
        var firstOutput = Directory.CreateDirectory(Path.Combine(root, "ux-output-a")).FullName;
        var secondOutput = Directory.CreateDirectory(Path.Combine(root, "ux-output-b")).FullName;
        var settingsFile = Path.Combine(data, "settings.json");
        File.WriteAllText(settingsFile, JsonSerializer.Serialize(new AppSettings { DownloadDirectory = firstOutput, EnableHardwareAcceleration = false, HasSeenEnginePrompt = true }));
        var settings = new SettingsService(data);
        var pickerA = new OutputLocationViewModel(settings);
        var pickerB = new OutputLocationViewModel(settings);
        var changed = 0;
        pickerB.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(OutputLocationViewModel.Path)) changed++; };
        Check(pickerA.ChangeDirectory(secondOutput) && pickerB.Path == secondOutput && changed == 1, "Shared output location did not synchronize");
        Check(!pickerA.ChangeDirectory(Path.Combine(root, "missing-directory")) && pickerB.Path == secondOutput && changed == 1, "Invalid output path must preserve previous setting");
        var persisted = File.ReadAllText(settingsFile);
        using (var locked = new FileStream(settingsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(!pickerA.ChangeDirectory(firstOutput) && pickerB.Path == secondOutput && File.ReadAllText(settingsFile) == persisted, "Settings persistence failure must preserve path and file");
        Check(!Directory.EnumerateFiles(data, "*.partial").Any(), "Failed settings write leaked staging files");
        Check(pickerA.ChangeDirectory(firstOutput) && !pickerA.HasError, "Changing output folder must recover after write failure");
        Check(new SettingsService(data).Settings.DownloadDirectory == firstOutput, "Output setting must persist across restart");
        var workspace = new MediaWorkspace(Path.Combine(data, "workspace.json"));
        var converter = new ConverterViewModel(ffmpeg, settings, dependency, workspace);
        converter.AddFiles(new[] { source });
        converter.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConverterViewModel.IsConverting) && converter.IsConverting)
                Check(pickerA.ChangeDirectory(secondOutput), "Change while starting batch");
        };
        await converter.StartConversionCommand.ExecuteAsync(null);
        Check(converter.ConversionList[0].Status == TaskState.Completed && converter.ConversionList[0].OutputPath.StartsWith(firstOutput + Path.DirectorySeparatorChar), "Accepted batch must retain original destination");

        var service = new ControlledDownloader(dependency);
        var downloader = new DownloaderViewModel(service, settings, dependency, workspace);
        var a = new TaskCompletionSource<MediaMetadata?>();
        var b = new TaskCompletionSource<MediaMetadata?>();
        service.Fetch = (url, _) => url.EndsWith("/a") ? a.Task : b.Task;
        downloader.UrlInput = "https://example.test/a";
        var requestA = downloader.FetchInfoCommand.ExecuteAsync(null);
        downloader.UrlInput = "https://example.test/b";
        var requestB = downloader.FetchInfoCommand.ExecuteAsync(null);
        a.SetResult(Metadata("old")); await requestA;
        Check(downloader.IsLoadingInfo && !downloader.HasMediaInfo && !downloader.StartDownloadCommand.CanExecute(null), "Old fetch completion must not clear newer loading state");
        b.SetResult(Metadata("latest")); await requestB;
        Check(downloader.CurrentMediaInfo?.Title == "latest" && downloader.StartDownloadCommand.CanExecute(null), "Latest URL metadata must enable download");
        // Opposite completion order; cancelled service ignores cancellation.
        a = new(); b = new();
        downloader.UrlInput = "https://example.test/a"; requestA = downloader.FetchInfoCommand.ExecuteAsync(null);
        downloader.UrlInput = "https://example.test/b"; requestB = downloader.FetchInfoCommand.ExecuteAsync(null);
        b.SetResult(Metadata("latest-first")); await requestB;
        a.SetResult(Metadata("stale-last")); await requestA;
        Check(downloader.CurrentMediaInfo?.Title == "latest-first" && !downloader.IsLoadingInfo, "Late old fetch must not overwrite the latest metadata");
        downloader.UrlInput = "https://example.test/c";
        Check(!downloader.HasMediaInfo && !downloader.StartDownloadCommand.CanExecute(null), "Editing URL must invalidate previous download data");
        service.Fetch = (_, _) => Task.FromResult<MediaMetadata?>(Metadata("new"));
        await downloader.FetchInfoCommand.ExecuteAsync(null);
        downloader.OutputFileName = "CON";
        Check(!downloader.StartDownloadCommand.CanExecute(null), "Invalid filename must disable download");
        downloader.OutputFileName = "valid";
        var acceptedPath = settings.Settings.DownloadDirectory;
        downloader.DownloadQueue.CollectionChanged += (_, _) => pickerA.ChangeDirectory(firstOutput);
        await downloader.StartDownloadCommand.ExecuteAsync(null);
        Check(service.AcceptedFolder == acceptedPath && downloader.UrlInput == "https://example.test/c" && downloader.HasMediaInfo, "Download must snapshot destination and retain form after failure");
        Check(ResultFileService.Open(Path.Combine(root, "missing-output"), true).Contains("ไม่พบ"), "Missing output must report its own failure, not open current default");
        Console.WriteLine("PASS: v1.1.2 shared output persistence/rollback, immutable destinations, URL races and download readiness");
    }
    private static MediaMetadata Metadata(string title) => new() { Title = title, Duration = TimeSpan.FromSeconds(1) };
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class ControlledDownloader(DependencyService dependency) : YtDlpService(dependency)
    {
        public Func<string, CancellationToken, Task<MediaMetadata?>> Fetch { get; set; } = (_, _) => Task.FromResult<MediaMetadata?>(null);
        public string AcceptedFolder { get; private set; } = "";
        public override Task<MediaMetadata?> GetMediaInfoAsync(string url, CancellationToken ct = default) => Fetch(url, ct);
        public override Task<MediaOperationResult> DownloadAsync(DownloadItem item, string outputFolder, CancellationToken ct = default)
        {
            AcceptedFolder = outputFolder;
            item.Status = TaskState.Failed;
            return Task.FromResult(MediaOperationResult.Failed("test failure"));
        }
    }
}
