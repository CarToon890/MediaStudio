using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.EventStream;
using MediaStudio.Models;

namespace MediaStudio.Services;

public class FFmpegService
{
    private readonly DependencyService _dependencyService;
    private static readonly Regex DurationRegex = new(@"Duration:\s+(\d+):(\d+):(\d+\.\d+)", RegexOptions.Compiled);
    private static readonly Regex TimeProgressRegex = new(@"time=(\d+):(\d+):(\d+\.\d+)", RegexOptions.Compiled);

    public FFmpegService(DependencyService dependencyService)
    {
        _dependencyService = dependencyService;
    }

    public async Task<TimeSpan> GetDurationAsync(string inputFilePath, CancellationToken ct = default)
    {
        if (!File.Exists(_dependencyService.FFmpegPath) || !File.Exists(inputFilePath)) return TimeSpan.Zero;

        try
        {
            var output = new System.Text.StringBuilder();
            var cmd = Cli.Wrap(_dependencyService.FFmpegPath)
                .WithArguments(new[] { "-i", inputFilePath })
                .WithValidation(CommandResultValidation.None);

            await foreach (var cmdEvent in cmd.ListenAsync(ct))
            {
                if (cmdEvent is StandardErrorCommandEvent stdErr)
                {
                    output.AppendLine(stdErr.Text);
                }
            }

            var match = DurationRegex.Match(output.ToString());
            if (match.Success)
            {
                var hours = int.Parse(match.Groups[1].Value);
                var minutes = int.Parse(match.Groups[2].Value);
                var seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                return TimeSpan.FromSeconds(hours * 3600 + minutes * 60 + seconds);
            }
        }
        catch { }

        return TimeSpan.Zero;
    }

    public async Task<bool> ConvertAsync(ConversionItem item, string outputFolder, bool useNvenc, CancellationToken ct = default)
    {
        if (!File.Exists(_dependencyService.FFmpegPath) || !File.Exists(item.SourceFilePath))
        {
            item.Status = TaskState.Failed;
            item.StatusMessage = "ไม่พบไฟล์ต้นทางหรือเอนจิน FFmpeg";
            return false;
        }

        try
        {
            Directory.CreateDirectory(outputFolder);
            item.Status = TaskState.Converting;
            item.StatusMessage = "กำลังเริ่มแปลงไฟล์...";
            item.Progress = 0;

            var duration = await GetDurationAsync(item.SourceFilePath, ct);
            var totalSeconds = duration.TotalSeconds > 0 ? duration.TotalSeconds : 1;

            var baseName = Path.GetFileNameWithoutExtension(item.SourceFilePath);
            var ext = item.TargetFormat.ToLowerInvariant();
            var outputFilePath = Path.Combine(outputFolder, $"{baseName}_converted.{ext}");

            if (string.Equals(Path.GetFullPath(item.SourceFilePath), Path.GetFullPath(outputFilePath), StringComparison.OrdinalIgnoreCase))
            {
                outputFilePath = Path.Combine(outputFolder, $"{baseName}_converted_{DateTime.Now:yyyyMMddHHmmss}.{ext}");
            }

            var (success, error) = await ExecuteConversionAsync(item, outputFilePath, totalSeconds, useNvenc, ct);

            // If hardware acceleration failed, attempt fallback to software CPU encoding
            if (!success && useNvenc && !ct.IsCancellationRequested)
            {
                item.StatusMessage = "การเร่งความเร็วด้วย GPU ไม่พร้อมใช้งาน สลับไปใช้ CPU...";
                (success, error) = await ExecuteConversionAsync(item, outputFilePath, totalSeconds, false, ct);
            }

            if (success && File.Exists(outputFilePath))
            {
                item.Progress = 100;
                item.Status = TaskState.Completed;
                item.StatusMessage = "แปลงไฟล์เสร็จสมบูรณ์";
                item.OutputPath = outputFilePath;
                return true;
            }

            item.Status = TaskState.Failed;
            item.StatusMessage = !string.IsNullOrWhiteSpace(error) 
                ? $"แปลงไฟล์ไม่สำเร็จ: {error}" 
                : "การแปลงไฟล์ล้มเหลว";
            return false;
        }
        catch (OperationCanceledException)
        {
            item.Status = TaskState.Cancelled;
            item.StatusMessage = "ยกเลิกการแปลงไฟล์";
            return false;
        }
        catch (Exception ex)
        {
            item.Status = TaskState.Failed;
            item.StatusMessage = $"เกิดข้อผิดพลาด: {ex.Message}";
            return false;
        }
    }

    private async Task<(bool Success, string? Error)> ExecuteConversionAsync(ConversionItem item, string outputFilePath, double totalSeconds, bool useNvenc, CancellationToken ct)
    {
        var ext = item.TargetFormat.ToLowerInvariant();
        var args = new System.Collections.Generic.List<string>
        {
            "-y",
            "-i", item.SourceFilePath
        };

        if (item.CompressVideo && item.TargetSizeMb > 0)
        {
            // Calculate target bitrate
            var targetBits = item.TargetSizeMb * 8L * 1024L * 1024L;
            var audioBitrate = 128L * 1024L; // 128kbps
            var videoBitrate = Math.Max(100L * 1024L, (long)(targetBits / totalSeconds) - audioBitrate);
            var videoBitrateK = (int)(videoBitrate / 1024);

            if (useNvenc)
            {
                args.AddRange(new[] { "-c:v", "h264_nvenc", "-b:v", $"{videoBitrateK}k", "-c:a", "aac", "-b:a", "128k" });
            }
            else
            {
                args.AddRange(new[] { "-c:v", "libx264", "-b:v", $"{videoBitrateK}k", "-preset", "fast", "-c:a", "aac", "-b:a", "128k" });
            }
        }
        else
        {
            // Normal conversion
            switch (ext)
            {
                case "mp3":
                    args.AddRange(new[] { "-vn", "-c:a", "libmp3lame", "-q:a", "2" });
                    break;
                case "wav":
                    args.AddRange(new[] { "-vn", "-c:a", "pcm_s16le" });
                    break;
                case "flac":
                    args.AddRange(new[] { "-vn", "-c:a", "flac" });
                    break;
                case "gif":
                    args.AddRange(new[] { "-vf", "fps=15,scale=480:-1:flags=lanczos" });
                    break;
                case "mp4":
                    if (useNvenc)
                        args.AddRange(new[] { "-c:v", "h264_nvenc", "-preset", "p4", "-cq", "23", "-c:a", "aac" });
                    else
                        args.AddRange(new[] { "-c:v", "libx264", "-crf", "22", "-preset", "medium", "-c:a", "aac" });
                    break;
                case "mkv":
                    args.AddRange(new[] { "-c:v", "copy", "-c:a", "copy" });
                    break;
                default:
                    args.AddRange(new[] { "-c:v", "libx264", "-c:a", "aac" });
                    break;
            }
        }

        args.Add(outputFilePath);

        var cmd = Cli.Wrap(_dependencyService.FFmpegPath)
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None);

        string? lastError = null;
        int exitCode = 0;

        await foreach (var cmdEvent in cmd.ListenAsync(ct))
        {
            if (cmdEvent is StandardErrorCommandEvent stdErr)
            {
                var text = stdErr.Text;
                var match = TimeProgressRegex.Match(text);
                if (match.Success)
                {
                    var hours = int.Parse(match.Groups[1].Value);
                    var minutes = int.Parse(match.Groups[2].Value);
                    var seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                    var currentSec = hours * 3600 + minutes * 60 + seconds;
                    var pct = Math.Min(99.0, (currentSec / totalSeconds) * 100);
                    item.Progress = Math.Round(pct, 1);
                    item.StatusMessage = $"กำลังประมวลผล: {item.Progress}%";
                }
                else if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith("frame=", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("size=", StringComparison.OrdinalIgnoreCase))
                {
                    lastError = text.Trim();
                }
            }
            else if (cmdEvent is ExitedCommandEvent exited)
            {
                exitCode = exited.ExitCode;
            }
        }

        return (exitCode == 0, lastError);
    }

    public async Task<bool> LosslessTrimAsync(string inputFilePath, TimeSpan start, TimeSpan end, string outputFolder, bool extractAudioOnly, CancellationToken ct = default)
    {
        if (!File.Exists(_dependencyService.FFmpegPath) || !File.Exists(inputFilePath)) return false;

        try
        {
            Directory.CreateDirectory(outputFolder);

            var baseName = Path.GetFileNameWithoutExtension(inputFilePath);
            var ext = extractAudioOnly ? "mp3" : Path.GetExtension(inputFilePath).TrimStart('.');
            var outputFilePath = Path.Combine(outputFolder, $"{baseName}_trimmed.{ext}");

            if (string.Equals(Path.GetFullPath(inputFilePath), Path.GetFullPath(outputFilePath), StringComparison.OrdinalIgnoreCase))
            {
                outputFilePath = Path.Combine(outputFolder, $"{baseName}_trimmed_{DateTime.Now:yyyyMMddHHmmss}.{ext}");
            }

            var startStr = start.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
            var duration = end - start;
            var durationStr = duration.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

            var args = new System.Collections.Generic.List<string>
            {
                "-y",
                "-ss", startStr,
                "-t", durationStr,
                "-i", inputFilePath
            };

            if (extractAudioOnly)
            {
                args.AddRange(new[] { "-vn", "-c:a", "libmp3lame", "-q:a", "2" });
            }
            else
            {
                // Lossless instant cut (no re-encoding) with timestamp alignment
                args.AddRange(new[] { "-c", "copy", "-avoid_negative_ts", "make_zero" });
            }

            args.Add(outputFilePath);

            var result = await Cli.Wrap(_dependencyService.FFmpegPath)
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync(ct);

            return result.ExitCode == 0 && File.Exists(outputFilePath);
        }
        catch
        {
            return false;
        }
    }
}
