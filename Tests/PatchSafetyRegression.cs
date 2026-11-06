using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaStudio.Models;
using MediaStudio.Services;
using MediaStudio.ViewModels;

internal static class PatchSafetyRegression
{
    public static async Task RunAsync(string root, string source, DependencyService dependency)
    {
        var gate = dependency.Operations;
        Check(gate.TryBeginWork(out var work), "Work lease");
        Check(!gate.TryBeginMaintenance(out _), "Active work must prevent update");
        work.Dispose(); work.Dispose();
        Check(gate.TryBeginMaintenance(out var update), "Update lease after work cleanup");
        Check(!gate.TryBeginWork(out _) && !gate.TryBeginMaintenance(out _), "Update must exclude all new operations");
        update.Dispose();
        Check(gate.TryBeginWork(out work), "Failed/finished update releases gate"); work.Dispose();

        var rollback = typeof(DependencyService).GetMethod("ReplaceEnginesAtomically", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var lockedIndex in new[] { 0, 1 })
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, "rollback-" + lockedIndex)).FullName;
            var paths = new[] { "yt.exe", "ff.exe", "new-yt.exe", "new-ff.exe" }.Select(x => Path.Combine(directory, x)).ToArray();
            foreach (var path in paths) File.WriteAllText(path, Path.GetFileName(path));
            using (File.Open(paths[lockedIndex], FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { rollback.Invoke(null, new object[] { paths[2], paths[3], paths[0], paths[1] }); throw new Exception("Expected locked replacement failure"); }
                catch (TargetInvocationException error) when (error.InnerException is IOException) { }
            }
            Check(File.ReadAllText(paths[0]) == "yt.exe" && File.ReadAllText(paths[1]) == "ff.exe", "Both original engines must survive any backup-stage failure");
        }
        var resolve = typeof(YtDlpService).GetMethod("ResolveOutputPath", BindingFlags.NonPublic | BindingFlags.Static)!;
        var incomplete = Path.Combine(root, "partial-only.mp4.part"); File.WriteAllText(incomplete, "unfinished");
        var resolved = (string)resolve.Invoke(null, new object?[] { incomplete, root, "partial-only", "mp4" })!;
        Check(resolved != incomplete && !File.Exists(resolved), "Partial media must never resolve as final output");
        foreach (var invalid in new[] { "--exec=anything", "file:///C:/example.mp4", "ftp://example.test/x", "https://name:secret@example.test/x" })
            Check(!YtDlpService.IsWebUrl(invalid), "Reject non-web/options/credential URL");
        var metadata = YtDlpService.ParseMetadata("""{"title":"sample","duration":null,"formats":[{"width":3840,"height":2160,"vcodec":"none"},{"width":1920,"height":1080,"vcodec":"h264"},{"width":9999,"height":9999,"protocol":"mhtml"},{"width":null,"height":null}]}""", "https://example.test/v");
        Check(metadata.AvailableVideoResolutions.SequenceEqual(new[] { new VideoDimensions(1920, 1080) }) && metadata.Duration == TimeSpan.Zero, "Metadata excludes audio/storyboards and tolerates unknown values");
        Check(new VideoDimensions(3840, 2160).Fit(1080) == new VideoDimensions(1920, 1080), "Landscape downscale");
        Check(new VideoDimensions(2160, 3840).Fit(1080) == new VideoDimensions(1080, 1920), "Portrait downscale");
        Check(new VideoDimensions(1920, 800).Fit(720) == new VideoDimensions(1280, 534), "Wide video preserves aspect within standard box");
        Check(new VideoDimensions(854, 480).Fit(480) == new VideoDimensions(854, 480)
            && new VideoDimensions(480, 854).Fit(480) == new VideoDimensions(480, 854), "Native rounded 480p must not be re-encoded");
        Check(new VideoDimensions(1920, 800).IsNativeFor(1080) && new VideoDimensions(854, 480).IsNativeFor(480)
            && !new VideoDimensions(1280, 720).IsNativeFor(1080), "Native widescreen and rounded formats should be selected without resizing");
        var formatBuilder = typeof(YtDlpService).GetMethod("BuildFormatArguments", BindingFlags.NonPublic | BindingFlags.Static)!;
        var nativeWide = new DownloadItem { Quality = "1080p", AvailableResolutions = new[] { new VideoDimensions(1920, 800), new VideoDimensions(3840, 1600) } };
        var selected = (string[])formatBuilder.Invoke(null, new object[] { nativeWide })!;
        Check(selected[1].StartsWith("bv*[width=1920][height=800]+ba/") && selected.Contains("--remux-video"), "Native wide format is preferred before resize fallback");
        Check(new VideoDimensions(1920, 1080).Fit(2160) == new VideoDimensions(1920, 1080), "Never upscale");

        var expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        await EngineIntegrity.VerifyAsync(source, expected, default);
        try { await EngineIntegrity.VerifyAsync(source, new string('0', 64), default); throw new Exception("Bad engine hash accepted"); }
        catch (InvalidDataException) { }
        using var client = new HttpClient(new ManifestHandler(expected));
        var asset = await EngineIntegrity.ResolveAsync(client, "yt-dlp/yt-dlp", "yt-dlp.exe", default);
        Check(asset.Hash == expected && asset.Url.Contains("/releases/download/v-test/"), "Resolve one release and matching digest before download");
        using var unverified = new HttpClient(new ManifestHandler(""));
        try { await EngineIntegrity.ResolveAsync(unverified, "yt-dlp/yt-dlp", "yt-dlp.exe", default); throw new Exception("Missing digest accepted"); }
        catch (InvalidDataException) { }

        var audio = Path.Combine(root, "audio-only.wav");
        await Generate(dependency.FFmpegPath, new[] { "-f", "lavfi", "-i", "sine=frequency=440:duration=0.2", audio });
        var probes = 0;
        using (var ffmpeg = new FFmpegService(dependency, new GpuCapabilityService((_, _) => { probes++; return Task.FromResult(true); })))
        {
            var conversion = new ConversionItem { SourceFilePath = audio, TargetFormat = "MP4" };
            Check((await ffmpeg.ConvertAsync(conversion, root, true)).Success && conversion.UsedEncoder == "aac" && probes == 0, $"Audio-only MP4 must not probe/report video encoder: {conversion.UsedEncoder}; probes={probes}; {conversion.StatusMessage}");
            var analysis = await ffmpeg.AnalyzeAudioAsync(audio);
            Check(analysis.Success && File.Exists(analysis.PreviewPath), "Preview fixture generated");
            ffmpeg.Dispose();
            Check(!File.Exists(analysis.PreviewPath), "Service disposal removes owned preview");
        }
        var native = Path.Combine(root, "native-1080.mp4");
        await Generate(dependency.FFmpegPath, new[] { "-f", "lavfi", "-i", "color=c=black:s=1920x1080:r=5:d=0.2", "-c:v", "libx264", "-pix_fmt", "yuv420p", native });
        var downloads = Directory.CreateDirectory(Path.Combine(root, "resolution-output")).FullName;
        var downloader = new NativeDownloader(dependency, native);
        var item = Download("720p");
        var terminal = 0; var resized = false;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(item.Status)) { if (item.Status == TaskState.Completed) terminal++; if (item.Status == TaskState.Converting) resized = item.IsRunning; } };
        var result = await downloader.DownloadAsync(item, downloads);
        Check(result.Success && item.SourceDimensions == new VideoDimensions(1920,1080) && item.OutputDimensions == new VideoDimensions(1280,720)
            && terminal == 1 && resized, "Download downscale and single terminal state");
        Check((await VideoInspectionService.InspectAsync(dependency.FFmpegPath, result.OutputPath)).Dimensions == item.OutputDimensions, "Final dimensions verified from actual file");
        var same = Download("4K (2160p)"); same.RequestedFileName = item.RequestedFileName;
        var originalBytes = File.ReadAllBytes(result.OutputPath);
        Check((await downloader.DownloadAsync(same, downloads)).Success && same.OutputDimensions == new VideoDimensions(1920,1080)
            && same.ResolutionSummary.Contains("ไม่ได้อัปสเกล") && same.OutputPath != item.OutputPath && File.ReadAllBytes(item.OutputPath).SequenceEqual(originalBytes), "No upscale and no overwrite");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var cancelledItem = Download("720p");
        Check(!(await downloader.DownloadAsync(cancelledItem, downloads, cancelled.Token)).Success && cancelledItem.Status == TaskState.Cancelled
            && File.Exists(cancelledItem.RetainedSourcePath), "Cancelled resize retains completed source");
        Check(!Directory.EnumerateDirectories(downloads, ".mediastudio-*").Any(), "Completed/cancelled jobs clean their staging directories");
        await VerifyUpdateBlocksNewWork(root, source, dependency);
        await CancelledExecutableStops(root);
        Console.WriteLine("PASS: engine rollback locks, update exclusion, partial-file rejection, URL/metadata validation, engine digests, audio encoder/cleanup, real download downscale/no-upscale/collision/cancel and child-process cancellation");
    }

    private static async Task VerifyUpdateBlocksNewWork(string root, string source, DependencyService dependency)
    {
        var data = Directory.CreateDirectory(Path.Combine(root, "maintenance-settings")).FullName;
        File.WriteAllText(Path.Combine(data, "settings.json"), JsonSerializer.Serialize(new AppSettings { DownloadDirectory = Path.Combine(root, "maintenance-output"), HasSeenEnginePrompt = true }));
        var settings = new SettingsService(data);
        var workspace = new MediaWorkspace(Path.Combine(data, "history.json"));
        using var ffmpeg = new FFmpegService(dependency);
        var downloader = new DownloaderViewModel(new YtDlpService(dependency), settings, dependency, workspace);
        var converter = new ConverterViewModel(ffmpeg, settings, dependency, workspace);
        var video = new TrimmerViewModel(ffmpeg, settings, dependency, workspace);
        var audio = new AudioTrimmerViewModel(ffmpeg, settings, dependency, workspace);
        var service = new PendingUpdate();
        var vm = new SettingsViewModel(settings, dependency, new Consent(), service, downloader, converter, video, audio);
        var main = new MainViewModel(new HomeViewModel(workspace), downloader, converter, video, audio,
            new WorkspaceViewModel(workspace), vm, dependency, settings, workspace, new Consent(), new OutputLocationViewModel(settings));
        await vm.CheckAppUpdateCommand.ExecuteAsync(null);
        var updating = vm.ApplyAppUpdateCommand.ExecuteAsync(null);
        Check(dependency.Operations.IsMaintenance && service.StageEntered, "Update holds lease throughout async staging");
        await main.SetupEnginesCommand.ExecuteAsync(null);
        Check(!main.IsEngineInitializing && main.AppStatus.Contains("รอ"), "Home engine setup must respect update exclusion");
        downloader.UrlInput = "https://example.test/video";
        await downloader.FetchInfoCommand.ExecuteAsync(null);
        converter.ConversionList.Add(new ConversionItem { SourceFilePath = source, TargetFormat = "MP4" });
        await converter.StartConversionCommand.ExecuteAsync(null);
        await video.LoadFileAsync(source);
        await audio.LoadFileAsync(source);
        await vm.CheckGpuCommand.ExecuteAsync(null);
        Check(!downloader.HasMediaInfo && !converter.IsConverting && converter.ConversionList[0].Status == TaskState.Queued
            && !video.HasLoadedFile && !audio.HasLoadedFile && vm.GpuStatus.Contains("รอ"), "All tools and GPU must refuse new work while update stages");
        service.Staged.SetException(new IOException("controlled staging failure"));
        await updating;
        Check(!dependency.Operations.IsMaintenance && !vm.IsAppUpdateBusy && !service.Launched, "Failed update releases lease without launching updater");
    }
    private sealed class Consent : IConfirmationService
    {
        public bool ConfirmEngineDownload(bool refresh) => true;
        public bool ConfirmApplicationUpdate(string current, string latest) => true;
    }
    private sealed class PendingUpdate : IAppUpdateService
    {
        public TaskCompletionSource<StagedAppUpdate> Staged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool StageEntered { get; private set; }
        public bool Launched { get; private set; }
        public Task<AppUpdateCheckResult> CheckLatestAsync(Version current, CancellationToken ct = default) => Task.FromResult(AppUpdateCheckResult.Completed(
            new AppReleaseInfo { TagName = "v99.0.0", Version = new Version(99, 0, 0), ReleaseUrl = "https://example.test/release", PackageUrl = "https://example.test/app.zip", ChecksumUrl = "https://example.test/app.sha256" }, true));
        public Task<StagedAppUpdate> StageUpdateAsync(AppReleaseInfo release, IProgress<AppUpdateProgress>? progress = null, CancellationToken cancellationToken = default)
        { StageEntered = true; return Staged.Task; }
        public UpdateLaunchResult LaunchUpdater(StagedAppUpdate update) { Launched = true; return UpdateLaunchResult.Failed("test must not launch"); }
        public bool CanWriteToApplicationDirectory(out string errorMessage) { errorMessage = ""; return true; }
        public void OpenReleasePage(string releaseUrl) => throw new Exception("test must not open browser");
    }

    private static DownloadItem Download(string quality) => new() { Url = "https://example.test/video", RequestedFileName = "download", Quality = quality, DurationSeconds = .2 };
    private static async Task Generate(string engine, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(engine) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(args)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
        Check(process.ExitCode == 0, "Generate synthetic fixture: " + error);
    }
    private static async Task CancelledExecutableStops(string root)
    {
        var pidFile = Path.Combine(root, "child.pid");
        var validate = typeof(DependencyService).GetMethod("ValidateExecutableAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        using var cancel = new CancellationTokenSource();
        var host = Environment.ProcessPath!;
        var dllArgument = Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? $"\"{Assembly.GetExecutingAssembly().Location}\" " : "";
        var task = (Task<bool>)validate.Invoke(null, new object[] { host, dllArgument + $"--engine-child \"{pidFile}\"", cancel.Token })!;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(pidFile) && !task.IsCompleted && DateTime.UtcNow < deadline) await Task.Delay(10);
        Check(File.Exists(pidFile), "Controlled validation child started");
        var pid = int.Parse(await File.ReadAllTextAsync(pidFile));
        try
        {
            cancel.Cancel();
            try { await task; throw new Exception("Validation cancellation swallowed"); } catch (OperationCanceledException) { }
            try { using var child = Process.GetProcessById(pid); Check(child.HasExited, "Cancelled validation child must exit"); }
            catch (ArgumentException) { }
        }
        finally { try { using var child = Process.GetProcessById(pid); if (!child.HasExited) child.Kill(true); } catch (ArgumentException) { } }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class NativeDownloader(DependencyService dependency, string source) : YtDlpService(dependency)
    {
        protected override Task<MediaOperationResult> DownloadCoreAsync(DownloadItem item, string folder, CancellationToken ct = default)
        {
            item.Status = TaskState.Downloading;
            var file = Path.Combine(folder, "native.mp4"); File.Copy(source, file);
            return Task.FromResult(MediaOperationResult.Completed(file));
        }
    }
    private sealed class ManifestHandler(string hash) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new { assets = new[] { new { name = "yt-dlp.exe", browser_download_url = "https://github.com/yt-dlp/yt-dlp/releases/download/v-test/yt-dlp.exe", digest = hash.Length == 0 ? null : "sha256:" + hash } } }), Encoding.UTF8, "application/json") });
    }
}
