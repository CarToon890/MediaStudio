using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using MediaStudio.Models;
using MediaStudio.Services;
using MediaStudio.ViewModels;

// Dependency-free integration checks. Pass the path to an installed FFmpeg executable.
if (args.Length != 1 || !File.Exists(args[0]))
    throw new ArgumentException("Pass the full path to ffmpeg.exe.");
var ffmpegPath = Path.GetFullPath(args[0]);
var root = Path.Combine(Path.GetTempPath(), "MediaStudio-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var dependency = new DependencyService();
    typeof(DependencyService).GetProperty(nameof(DependencyService.FFmpegPath))!.SetValue(dependency, ffmpegPath);
    // No yt-dlp process is used; an existing path keeps dependency checks offline.
    typeof(DependencyService).GetProperty(nameof(DependencyService.YtDlpPath))!.SetValue(dependency, ffmpegPath);
    var ffmpeg = new FFmpegService(dependency);
    var source = Path.Combine(root, "clip.mp4");
    var start = new ProcessStartInfo(ffmpegPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
    foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=64x64:rate=10", "-f", "lavfi", "-i", "sine=frequency=440", "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", source })
        start.ArgumentList.Add(arg);
    using (var process = Process.Start(start)!)
    {
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Check(process.ExitCode == 0, "Generate media: " + error);
    }

    var output = Path.Combine(root, "output");
    Directory.CreateDirectory(output);
    var existing = Path.Combine(output, "clip_converted.mp4");
    await File.WriteAllTextAsync(existing, "existing result");
    var first = Item(source);
    Check((await ffmpeg.ConvertAsync(first, output, false)).Success, first.StatusMessage);
    var firstBytes = await File.ReadAllBytesAsync(first.OutputPath);
    var otherFolder = Directory.CreateDirectory(Path.Combine(root, "other")).FullName;
    var otherSource = Path.Combine(otherFolder, "clip.mp4");
    File.Copy(source, otherSource);
    var second = Item(otherSource);
    Check((await ffmpeg.ConvertAsync(second, output, false)).Success, second.StatusMessage);
    Check(first.OutputPath != second.OutputPath, "Same-name inputs must have distinct outputs");
    Check(await File.ReadAllTextAsync(existing) == "existing result", "Existing output changed");
    Check(firstBytes.SequenceEqual(await File.ReadAllBytesAsync(first.OutputPath)), "First result overwritten");
    Console.WriteLine("PASS: existing outputs and same-basename results preserved");

    foreach (var format in new[] { "MP3", "GIF", "WAV", "FLAC", "MP4", "MKV" })
    {
        var item = Item(source, format);
        item.CompressVideo = true;
        item.TargetSizeMb = 5;
        Check((await ffmpeg.ConvertAsync(item, output, false)).Success, format + ": " + item.StatusMessage);
        Check(new FileInfo(item.OutputPath).Length > 0, "Empty " + format);
    }
    Console.WriteLine("PASS: all six target formats with compression requested");

    // Avoid reading/writing the user's settings file in these checks.
    var settings = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
    typeof(SettingsService).GetField("<Settings>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(settings, new AppSettings { DownloadDirectory = output, EnableHardwareAcceleration = false });
    var workspacePath = Path.Combine(root, "workspace", "history.json");
    var workspace = new MediaWorkspace(workspacePath);
    var vm = new ConverterViewModel(ffmpeg, settings, dependency, workspace);
    vm.IsCompressEnabled = true;
    vm.SelectedTargetFormat = "MP3";
    Check(!vm.CanCompressVideo && !vm.IsCompressEnabled, "Unsupported compression toggle not cleared");
    vm.SelectedTargetFormat = "MP4";
    var running = Item(source);
    var removed = Item(source);
    var remaining = Item(source);
    var added = Item(source);
    var completed = Item(source);
    completed.Status = TaskState.Completed;
    foreach (var item in new[] { running, removed, remaining, completed }) vm.ConversionList.Add(item);
    var changed = false;
    running.PropertyChanged += (_, e) =>
    {
        if (changed || e.PropertyName != nameof(ConversionItem.Status) || running.Status != TaskState.Converting) return;
        changed = true;
        vm.RemoveItemCommand.Execute(removed);
        vm.ConversionList.Add(added);
        vm.ClearCompletedCommand.Execute(null);
    };
    await vm.StartConversionCommand.ExecuteAsync(null);
    Check(changed && !vm.IsConverting, "Queue mutation or final reset failed");
    Check(running.Status == TaskState.Completed && remaining.Status == TaskState.Completed, "Original batch aborted");
    Check(removed.Status == TaskState.Queued && added.Status == TaskState.Queued, "Removed/new items incorrectly processed");
    Console.WriteLine("PASS: add/remove/clear during conversion; remaining batch completes");

    vm.ConversionList.Clear();
    var cancelled = Item(source);
    vm.ConversionList.Add(cancelled);
    cancelled.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(ConversionItem.Status) && cancelled.Status == TaskState.Converting)
            vm.CancelAllCommand.Execute(null);
    };
    await vm.StartConversionCommand.ExecuteAsync(null);
    Check(!vm.IsConverting && cancelled.Status == TaskState.Cancelled, "Cancellation did not settle");
    Console.WriteLine("PASS: cancellation resets batch state");

    Check(FileNameService.TryNormalizeBaseName("เพลงทดสอบ", out var thaiName, out _) && thaiName == "เพลงทดสอบ", "Thai file name rejected");
    Check(!FileNameService.TryNormalizeBaseName("   ", out _, out _), "Blank file name accepted");
    Check(!FileNameService.TryNormalizeBaseName("CON", out _, out _), "Reserved Windows name accepted");
    Check(!FileNameService.TryNormalizeBaseName("CON.txt", out _, out _), "Reserved Windows name with suffix accepted");
    Check(!FileNameService.TryNormalizeBaseName("bad:name", out _, out _), "Invalid Windows character accepted");
    Check(!FileNameService.TryNormalizeBaseName(new string('a', 181), out _, out _), "Overlong file name accepted");
    await File.WriteAllTextAsync(Path.Combine(output, "เพลงทดสอบ.mp3"), "existing");
    Check(FileNameService.MakeUniqueBaseName(output, "เพลงทดสอบ", "mp3") == "เพลงทดสอบ (1)", "Duplicate name not suffixed");
    Console.WriteLine("PASS: output file-name validation and collision handling");

    var waveform = await ffmpeg.AnalyzeAudioAsync(source, 120);
    Check(waveform.Success && waveform.Peaks.Count > 10 && waveform.Duration.TotalSeconds > 0, "Waveform analysis failed");
    Check(File.Exists(waveform.PreviewPath), "Audio preview was not generated");
    File.Delete(waveform.PreviewPath);
    var noAudioSource = Path.Combine(root, "no-audio.mp4");
    var noAudioStart = new ProcessStartInfo(ffmpegPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
    foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=64x64:rate=10", "-t", "0.3", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-an", noAudioSource }) noAudioStart.ArgumentList.Add(arg);
    using (var process = Process.Start(noAudioStart)!)
    {
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Check(process.ExitCode == 0, "Generate no-audio media: " + error);
    }
    Check(!(await ffmpeg.AnalyzeAudioAsync(noAudioSource, 50)).Success, "Video without audio was accepted by Audio Trimmer");
    foreach (var format in new[] { "MP3", "WAV", "FLAC" })
    {
        var trim = await ffmpeg.TrimAudioAsync(source, TimeSpan.FromSeconds(.1), TimeSpan.FromSeconds(.8), output, format);
        Check(trim.Success && File.Exists(trim.OutputPath) && new FileInfo(trim.OutputPath).Length > 0, "Audio trim failed: " + format);
        workspace.Register(trim.OutputPath, "Audio Trimmer", .7);
    }
    var quietTrim = await ffmpeg.TrimAudioAsync(source, TimeSpan.Zero, TimeSpan.FromSeconds(.8), output, "WAV", 50);
    var boostedTrim = await ffmpeg.TrimAudioAsync(source, TimeSpan.Zero, TimeSpan.FromSeconds(.8), output, "WAV", 150);
    Check(quietTrim.Success && boostedTrim.Success, "Volume-adjusted audio trim failed");
    var quietWaveform = await ffmpeg.AnalyzeAudioAsync(quietTrim.OutputPath, 80);
    var boostedWaveform = await ffmpeg.AnalyzeAudioAsync(boostedTrim.OutputPath, 80);
    var quietPeak = quietWaveform.Peaks.DefaultIfEmpty().Max();
    var boostedPeak = boostedWaveform.Peaks.DefaultIfEmpty().Max();
    Check(quietWaveform.Success && boostedWaveform.Success && boostedPeak > quietPeak * 2.4,
        $"Audio gain was not applied: quiet={quietPeak:F3}, boosted={boostedPeak:F3}");
    File.Delete(quietWaveform.PreviewPath);
    File.Delete(boostedWaveform.PreviewPath);
    var audioVm = new AudioTrimmerViewModel(ffmpeg, settings, dependency, workspace) { VolumePercent = 150 };
    Check(audioVm.VolumeDisplay.Contains("150%") && audioVm.VolumeDisplay.Contains("+3.5 dB"), "Volume percentage/dB label is incorrect");
    audioVm.VolumePercent = 0;
    Check(audioVm.VolumeDisplay.Contains("ปิดเสียง"), "Muted volume label is incorrect");
    var reloadedWorkspace = new MediaWorkspace(workspacePath);
    Check(reloadedWorkspace.Assets.Count == workspace.Assets.Count && reloadedWorkspace.Assets.Count(x => x.Kind == MediaKind.Audio) == 3,
        $"Workspace persistence failed: count={reloadedWorkspace.Assets.Count}, kinds={string.Join(',', reloadedWorkspace.Assets.Select(x => x.Kind))}");
    var routed = false;
    reloadedWorkspace.TransferRequested += (_, target) => routed = target == ToolDestination.Converter;
    reloadedWorkspace.RequestTransfer(reloadedWorkspace.Assets[0], ToolDestination.Converter);
    Check(routed, "Workspace transfer request failed");
    var missingPath = Path.Combine(root, "missing.mp3");
    await File.WriteAllTextAsync(missingPath, "temporary");
    workspace.Register(missingPath, "ไฟล์นำเข้า");
    File.Delete(missingPath);
    Check(!new MediaWorkspace(workspacePath).Assets.Any(x => x.FilePath == missingPath), "Missing workspace file was not removed on reload");
    Console.WriteLine("PASS: waveform, audio trimming, workspace persistence and routing");

    var handler = new DownloadHandler();
    using var client = new HttpClient(handler);
    typeof(DependencyService).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dependency, client);
    var download = typeof(DependencyService).GetMethod("DownloadFileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var destination = Path.Combine(root, "engine.exe");
    Task Download(CancellationToken ct = default) => (Task)download.Invoke(dependency, new object[] { "https://test.invalid/engine", destination, "test", ct })!;
    handler.Truncated = true;
    var failed = false;
    try { await Download(); } catch (IOException) { failed = true; }
    Check(failed && !File.Exists(destination), "Partial executable was published");
    Check(!Directory.EnumerateFiles(root, "*.partial").Any(), "Partial download was not cleaned up");
    handler.Truncated = false;
    await Download();
    Check((await File.ReadAllBytesAsync(destination)).SequenceEqual(handler.Payload), "Retry did not publish complete file");
    using var cts = new CancellationTokenSource();
    dependency.DownloadProgressChanged += (_, _) => cts.Cancel();
    failed = false;
    try { await Download(cts.Token); } catch (OperationCanceledException) { failed = true; }
    Check(failed && (await File.ReadAllBytesAsync(destination)).SequenceEqual(handler.Payload), "Cancelled download replaced existing executable");
    Check(!Directory.EnumerateFiles(root, "*.partial").Any(), "Cancelled download left partial files");
    Console.WriteLine("PASS: interrupted/cancelled downloads stay unpublished; retry succeeds");
}
finally
{
    Directory.Delete(root, recursive: true);
}

static ConversionItem Item(string source, string format = "MP4") => new()
{
    SourceFilePath = source, FileName = Path.GetFileName(source), TargetFormat = format, Status = TaskState.Queued
};
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
sealed class DownloadHandler : HttpMessageHandler
{
    public bool Truncated { get; set; }
    public byte[] Payload { get; } = new byte[] { 1, 2, 3, 4 };
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var content = new ByteArrayContent(Payload);
        if (Truncated) content.Headers.ContentLength = Payload.Length + 10;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
