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
    private static readonly Regex ProgressRegex = new(@"\[download\]\s+([\d\.]+)%\s+of\s+~?([\d\.]+\w+)\s+at\s+([\d\.]+\w+/s)\s+ETA\s+([\d:]+)", RegexOptions.Compiled);
    private static readonly Regex DestinationRegex = new(@"\[(?:Merger|download|ExtractAudio)\]\s+Destination:\s+(.+)", RegexOptions.Compiled);
    private static readonly Regex MergedRegex = new(@"\[Merger\]\s+Merging formats into ""?([^""\r\n]+)""?", RegexOptions.Compiled);
    private static readonly Regex AlreadyDownloadedRegex = new(@"\[download\]\s+(.+?)\s+has already been downloaded", RegexOptions.Compiled);
    private const string FinalPathPrefix = "__MEDIASTUDIO_FILE__";

    public YtDlpService(DependencyService dependencyService)
    {
        _dependencyService = dependencyService;
    }

    public async Task<MediaMetadata?> GetMediaInfoAsync(string url, CancellationToken ct = default)
    {
        if (!File.Exists(_dependencyService.YtDlpPath)) return null;

        try
        {
            var jsonBuilder = new System.Text.StringBuilder();
            var cmd = Cli.Wrap(_dependencyService.YtDlpPath)
                .WithArguments(new[] { "--dump-single-json", "--no-playlist", "--skip-download", url })
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

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "Unknown" : "Unknown";
            var uploader = root.TryGetProperty("uploader", out var u) ? u.GetString() ?? "" : "";
            var thumbnail = root.TryGetProperty("thumbnail", out var th) ? th.GetString() ?? "" : "";
            var durationSec = root.TryGetProperty("duration", out var d) ? d.GetDouble() : 0;
            var extractor = root.TryGetProperty("extractor_key", out var ex) ? ex.GetString() ?? "" : "";

            return new MediaMetadata
            {
                Title = title,
                Author = uploader,
                ThumbnailUrl = thumbnail,
                Duration = TimeSpan.FromSeconds(durationSec),
                Url = url,
                Extractor = extractor
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<MediaOperationResult> DownloadAsync(DownloadItem item, string outputFolder, CancellationToken ct = default)
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
                var heightLimit = item.Quality switch
                {
                    "4K (2160p)" => "2160",
                    "2K (1440p)" => "1440",
                    "1080p" => "1080",
                    "720p" => "720",
                    "480p" => "480",
                    _ => "1080"
                };

                arguments.Add("-f");
                arguments.Add($"bestvideo[height<={heightLimit}]+bestaudio/best[height<={heightLimit}]/best");
                arguments.Add("--merge-output-format");
                arguments.Add("mp4");
            }

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
                    ? $"ดาวน์โหลดไม่สำเร็จ: {lastError}" 
                    : $"ดาวน์โหลดไม่สำเร็จ (รหัสข้อผิดพลาด {exitCode})";
                return MediaOperationResult.Failed(item.StatusMessage);
            }

            item.Progress = 100;
            item.Status = TaskState.Completed;
            item.StatusMessage = "ดาวน์โหลดเสร็จสมบูรณ์";
            item.OutputPath = ResolveOutputPath(finalDestination, outputFolder, requestedName, item.IsAudioOnly ? "mp3" : "mp4");
            if (!File.Exists(item.OutputPath))
            {
                item.Status = TaskState.Failed;
                item.StatusMessage = "ดาวน์โหลดเสร็จแต่ไม่พบไฟล์ผลลัพธ์";
                return MediaOperationResult.Failed(item.StatusMessage);
            }
            return MediaOperationResult.Completed(item.OutputPath);
        }
        catch (OperationCanceledException)
        {
            item.Status = TaskState.Cancelled;
            item.StatusMessage = "ยกเลิกการดาวน์โหลด";
            return MediaOperationResult.Failed(item.StatusMessage);
        }
        catch (Exception ex)
        {
            item.Status = TaskState.Failed;
            item.StatusMessage = $"เกิดข้อผิดพลาด: {ex.Message}";
            return MediaOperationResult.Failed(item.StatusMessage);
        }
    }

    private static string ResolveOutputPath(string? reportedPath, string outputFolder, string requestedName, string expectedExtension)
    {
        if (!string.IsNullOrWhiteSpace(reportedPath))
        {
            var cleaned = reportedPath.Trim().Trim('"');
            if (!Path.IsPathRooted(cleaned)) cleaned = Path.Combine(outputFolder, cleaned);
            if (File.Exists(cleaned)) return Path.GetFullPath(cleaned);
        }

        var exact = Path.Combine(outputFolder, $"{requestedName}.{expectedExtension}");
        if (File.Exists(exact)) return exact;
        return Directory.EnumerateFiles(outputFolder, requestedName + ".*")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault() ?? exact;
    }
}
