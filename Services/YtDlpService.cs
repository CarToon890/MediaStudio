using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.EventStream;
using MediaStudio.Models;

namespace MediaStudio.Services;

public class YtDlpService
{
    private readonly DependencyService _dependencyService;
    private readonly DownloadVideoProcessor _videoProcessor;
    private static readonly Regex ProgressRegex = new(@"\[download\]\s+([\d\.]+)%\s+of\s+~?([\d\.]+\w+)\s+at\s+([\d\.]+\w+/s)\s+ETA\s+([\d:]+)", RegexOptions.Compiled);
    private static readonly Regex DestinationRegex = new(@"\[(?:Merger|download|ExtractAudio)\]\s+Destination:\s+(.+)", RegexOptions.Compiled);
    private static readonly Regex MergedRegex = new(@"\[Merger\]\s+Merging formats into ""?([^""\r\n]+)""?", RegexOptions.Compiled);
    private static readonly Regex AlreadyDownloadedRegex = new(@"\[download\]\s+(.+?)\s+has already been downloaded", RegexOptions.Compiled);
    private const string FinalPathPrefix = "__MEDIASTUDIO_FILE__";

    public YtDlpService(DependencyService dependencyService, GpuCapabilityService? gpuCapabilities = null)
    {
        _dependencyService = dependencyService;
        _videoProcessor = new DownloadVideoProcessor(dependencyService, gpuCapabilities ?? new GpuCapabilityService());
    }

    public virtual async Task<MediaMetadata?> GetMediaInfoAsync(string url, CancellationToken ct = default)
    {
        if (!IsWebUrl(url) || !File.Exists(_dependencyService.YtDlpPath)) return null;

        try
        {
            var jsonBuilder = new System.Text.StringBuilder();
            var cmd = Cli.Wrap(_dependencyService.YtDlpPath)
                .WithArguments(new[] { "--ignore-config", "--no-plugin-dirs", "--dump-single-json", "--no-playlist", "--skip-download", "--", url })
                .WithValidation(CommandResultValidation.None);

            await foreach (var cmdEvent in cmd.ListenAsync(ct))
            {
                if (cmdEvent is StandardOutputCommandEvent stdOut)
                {
                    jsonBuilder.AppendLine(stdOut.Text);
                }
            }

            var json = jsonBuilder.ToString().Trim();
            if (string.IsNullOrEmpty(json)) return null;

            return ParseMetadata(json, url);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsWebUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && parsed.Scheme is "https" or "http" && !string.IsNullOrWhiteSpace(parsed.Host) && string.IsNullOrEmpty(parsed.UserInfo);

    public static MediaMetadata ParseMetadata(string json, string url)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        static string Text(JsonElement node, string key) => node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        static VideoDimensions? Size(JsonElement node)
        {
            if (node.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out var width)
                && node.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var height) && width > 0 && height > 0 && width <= 65536 && height <= 65536)
                return new(width, height);
            return null;
        }
        var sizes = new List<VideoDimensions>();
        if (root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
            foreach (var format in formats.EnumerateArray())
                if (Text(format, "vcodec") != "none" && Text(format, "protocol") != "mhtml"
                    && Text(format, "ext") is not ("jpg" or "png" or "webp") && Size(format) is { } size) sizes.Add(size);
        if (sizes.Count == 0 && Size(root) is { } fallback) sizes.Add(fallback);
        var duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetDouble(out var seconds)
            && double.IsFinite(seconds) && seconds >= 0 && seconds < TimeSpan.MaxValue.TotalSeconds ? seconds : 0;
        return new MediaMetadata { Title = Text(root, "title"), Author = Text(root, "uploader"), ThumbnailUrl = Text(root, "thumbnail"),
            Duration = TimeSpan.FromSeconds(duration), Url = url, Extractor = Text(root, "extractor_key"), AvailableVideoResolutions = sizes.Distinct().ToArray() };
    }

    public virtual async Task<MediaOperationResult> DownloadAsync(DownloadItem item, string outputFolder, CancellationToken ct = default)
    {
        if (!IsWebUrl(item.Url)) return Fail(item, "กรุณาใช้ลิงก์ HTTP หรือ HTTPS ที่ถูกต้อง");
        Directory.CreateDirectory(outputFolder);
        var stage = Path.Combine(outputFolder, ".mediastudio-" + Guid.NewGuid().ToString("N"));
        var name = FileNameService.SanitizeSuggestedName(item.RequestedFileName);
        string? native = null;
        Directory.CreateDirectory(stage);
        try
        {
            var downloaded = await DownloadCoreAsync(item, stage, ct);
            if (!downloaded.Success) return downloaded;
            native = downloaded.OutputPath;
            var result = item.IsAudioOnly ? native : await _videoProcessor.ProcessAsync(native, item, stage, ct);
            ct.ThrowIfCancellationRequested();
            var final = PublishFile(result, outputFolder, name);
            item.OutputPath = final;
            item.Progress = 100;
            item.Status = TaskState.Completed;
            item.StatusMessage = "ดาวน์โหลดเสร็จสมบูรณ์";
            return MediaOperationResult.Completed(final);
        }
        catch (NotSupportedException error)
        {
            PreserveSource();
            return Fail(item, error.Message + SavedSourceMessage());
        }
        catch (OperationCanceledException)
        {
            PreserveSource();
            item.Status = TaskState.Cancelled;
            item.StatusMessage = "ยกเลิกแล้ว" + SavedSourceMessage();
            return MediaOperationResult.Failed(item.StatusMessage);
        }
        catch
        {
            PreserveSource();
            return Fail(item, "ไม่สามารถดาวน์โหลดหรือลดความละเอียดได้ ตรวจสอบไฟล์ต้นทางและพื้นที่ปลายทาง" + SavedSourceMessage());
        }
        finally
        {
            // Only this job's generated staging directory is eligible for cleanup.
            if (!string.Equals(Path.GetDirectoryName(item.RetainedSourcePath), stage, StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(stage, true); } catch { }
        }
        string SavedSourceMessage() => string.IsNullOrEmpty(item.RetainedSourcePath) ? "" : $" • เก็บต้นทางไว้ที่ {item.RetainedSourcePath}";
        void PreserveSource()
        {
            if (native == null || !File.Exists(native) || new FileInfo(native).Length == 0) return;
            try { item.RetainedSourcePath = PublishFile(native, outputFolder, name + "_original"); }
            catch { item.RetainedSourcePath = native; }
        }
    }

    private static MediaOperationResult Fail(DownloadItem item, string message)
    { item.Status = TaskState.Failed; item.StatusMessage = message; return MediaOperationResult.Failed(message); }

    private static string PublishFile(string source, string folder, string name)
    {
        for (var index = 0; ; index++)
        {
            var target = Path.Combine(folder, name + (index == 0 ? "" : $" ({index})") + Path.GetExtension(source));
            try { File.Move(source, target, false); return target; }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target)) { }
        }
    }

    internal static string[] BuildFormatArguments(DownloadItem item)
    {
        var target = VideoDimensions.TargetHeight(item.Quality);
        var native = item.AvailableResolutions.Where(x => x.IsNativeFor(target)).OrderByDescending(x => (long)x.Width * x.Height).FirstOrDefault();
        var selector = native == null ? "bv*+ba/bv*/b"
            : $"bv*[width={native.Width}][height={native.Height}]+ba/bv*[width={native.Width}][height={native.Height}]/bv*+ba/bv*/b";
        return new[] { "-f", selector, "-S", $"+res:{target}", "--merge-output-format", "mp4", "--remux-video", "mp4" };
    }

    protected virtual async Task<MediaOperationResult> DownloadCoreAsync(DownloadItem item, string outputFolder, CancellationToken ct = default)
    {
        if (!File.Exists(_dependencyService.YtDlpPath))
        {
            item.Status = TaskState.Failed;
            item.StatusMessage = "ไม่พบเอนจิน yt-dlp";
            return MediaOperationResult.Failed(item.StatusMessage);
        }

        try
        {
            Directory.CreateDirectory(outputFolder);
            item.Status = TaskState.Downloading;
            item.StatusMessage = "กำลังเริ่มดาวน์โหลด...";
            item.Progress = 0;

            var requestedName = FileNameService.SanitizeSuggestedName(item.RequestedFileName);
            requestedName = FileNameService.MakeUniqueBaseName(outputFolder, requestedName, item.IsAudioOnly ? "mp3" : "mp4");
            item.RequestedFileName = requestedName;
            var outputTemplate = Path.Combine(outputFolder, requestedName + ".%(ext)s");

            var arguments = new System.Collections.Generic.List<string>
            {
                "--ignore-config", "--no-plugin-dirs",
                "--newline",
                "--no-playlist",
                "--progress",
                "--no-overwrites",
                "--no-simulate",
                "--print", $"after_move:{FinalPathPrefix}%(filepath)s",
                "-o", outputTemplate
            };

            if (File.Exists(_dependencyService.FFmpegPath))
            {
                arguments.Add("--ffmpeg-location");
                arguments.Add(_dependencyService.FFmpegPath);
            }

            if (item.IsAudioOnly)
            {
                arguments.Add("-x");
                arguments.Add("--audio-format");
                arguments.Add(item.Quality.ToLowerInvariant().Contains("flac") ? "flac" : "mp3");
                arguments.Add("--audio-quality");
                arguments.Add("0");
            }
            else
            {
                arguments.AddRange(BuildFormatArguments(item));
            }
            arguments.Add("--");

            arguments.Add(item.Url);

            var cmd = Cli.Wrap(_dependencyService.YtDlpPath)
                .WithArguments(arguments)
                .WithValidation(CommandResultValidation.None);

            string? finalDestination = null;
            string? lastError = null;
            int exitCode = 0;

            await foreach (var cmdEvent in cmd.ListenAsync(ct))
            {
                if (cmdEvent is StandardOutputCommandEvent stdOut)
                {
                    var line = stdOut.Text;

                    if (line.StartsWith(FinalPathPrefix, StringComparison.Ordinal))
                    {
                        finalDestination = line[FinalPathPrefix.Length..].Trim();
                    }

                    var match = ProgressRegex.Match(line);
                    if (match.Success)
                    {
                        if (double.TryParse(match.Groups[1].Value, out var percent))
                        {
                            item.Progress = percent;
                        }
                        item.Speed = match.Groups[3].Value;
                        item.Eta = match.Groups[4].Value;
                        item.StatusMessage = $"กำลังโหลด: {match.Groups[1].Value}% ({item.Speed})";
                    }

                    var destMatch = DestinationRegex.Match(line);
                    if (destMatch.Success)
                    {
                        finalDestination = destMatch.Groups[1].Value.Trim();
                    }

                    var mergeMatch = MergedRegex.Match(line);
                    if (mergeMatch.Success)
                    {
                        finalDestination = mergeMatch.Groups[1].Value.Trim();
                    }

                    var alreadyMatch = AlreadyDownloadedRegex.Match(line);
                    if (alreadyMatch.Success)
                    {
                        finalDestination = alreadyMatch.Groups[1].Value.Trim();
                    }
                }
                else if (cmdEvent is StandardErrorCommandEvent stdErr)
                {
                    var errLine = stdErr.Text.Trim();
                    if (!string.IsNullOrWhiteSpace(errLine) && !errLine.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
                    {
                        lastError = errLine;
                    }
                }
                else if (cmdEvent is ExitedCommandEvent exited)
                {
                    exitCode = exited.ExitCode;
                }
            }

            if (exitCode != 0)
            {
                item.Status = TaskState.Failed;
                item.StatusMessage = !string.IsNullOrWhiteSpace(lastError)
                    ? $"ดาวน์โหลดไม่สำเร็จ: {Regex.Replace(lastError, @"https?://[^\s]+", "[ลิงก์]")}"
                    : $"ดาวน์โหลดไม่สำเร็จ (รหัสข้อผิดพลาด {exitCode})";
                return MediaOperationResult.Failed(item.StatusMessage);
            }

            item.Progress = 100;
            var resolvedPath = ResolveOutputPath(finalDestination, outputFolder, requestedName, item.IsAudioOnly ? "mp3" : "mp4");
            if (!File.Exists(resolvedPath) || new FileInfo(resolvedPath).Length == 0)
            {
                item.Status = TaskState.Failed;
                item.StatusMessage = "ดาวน์โหลดเสร็จแต่ไม่พบไฟล์ผลลัพธ์";
                return MediaOperationResult.Failed(item.StatusMessage);
            }
            return MediaOperationResult.Completed(resolvedPath);
        }
        catch (OperationCanceledException)
        {
            item.Status = TaskState.Cancelled;
            item.StatusMessage = "ยกเลิกการดาวน์โหลด";
            return MediaOperationResult.Failed(item.StatusMessage);
        }
        catch (Exception)
        {
            item.Status = TaskState.Failed;
            item.StatusMessage = "เกิดข้อผิดพลาดในการดาวน์โหลด กรุณาตรวจสอบลิงก์และส่วนประกอบ";
            return MediaOperationResult.Failed(item.StatusMessage);
        }
    }

    private static string ResolveOutputPath(string? reportedPath, string outputFolder, string requestedName, string expectedExtension)
    {
        var exact = Path.GetFullPath(Path.Combine(outputFolder, $"{requestedName}.{expectedExtension}"));
        if (!string.IsNullOrWhiteSpace(reportedPath))
        {
            var cleaned = reportedPath.Trim().Trim('"');
            if (!Path.IsPathRooted(cleaned)) cleaned = Path.Combine(outputFolder, cleaned);
            cleaned = Path.GetFullPath(cleaned);
            if (string.Equals(Path.GetDirectoryName(cleaned), Path.GetFullPath(outputFolder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(cleaned), "." + expectedExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(cleaned)) return cleaned;
        }
        return exact; // Never select arbitrary .part/.ytdl/fragment files by glob.
    }
}
