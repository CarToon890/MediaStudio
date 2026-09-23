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

    public async Task<MediaOperationResult> ConvertAsync(ConversionItem item, string outputFolder, bool useNvenc, CancellationToken ct = default)
    {
        if (!File.Exists(_dependencyService.FFmpegPath) || !File.Exists(item.SourceFilePath))
        {
            item.Status = TaskState.Failed;
            item.StatusMessage = "ไม่พบไฟล์ต้นทางหรือเอนจิน FFmpeg";
            return MediaOperationResult.Failed(item.StatusMessage);
        }

        string? outputFilePath = null;
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
            outputFilePath = ReserveOutputPath(outputFolder, $"{baseName}_converted", ext);

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
                return MediaOperationResult.Completed(outputFilePath);
            }

            item.Status = TaskState.Failed;
            item.StatusMessage = !string.IsNullOrWhiteSpace(error) 
                ? $"แปลงไฟล์ไม่สำเร็จ: {error}" 
                : "การแปลงไฟล์ล้มเหลว";
            TryDeleteOutput(outputFilePath);
            return MediaOperationResult.Failed(item.StatusMessage);
        }
        catch (OperationCanceledException)
        {
            TryDeleteOutput(outputFilePath);
            item.Status = TaskState.Cancelled;
            item.StatusMessage = "ยกเลิกการแปลงไฟล์";
            return MediaOperationResult.Failed(item.StatusMessage);
        }
        catch (Exception ex)
        {
            TryDeleteOutput(outputFilePath);
            item.Status = TaskState.Failed;
            item.StatusMessage = $"เกิดข้อผิดพลาด: {ex.Message}";
            return MediaOperationResult.Failed(item.StatusMessage);
        }
    }

    private static string ReserveOutputPath(string outputFolder, string baseName, string extension)
    {
        for (var suffix = 0; ; suffix++)
        {
            var name = suffix == 0 ? baseName : $"{baseName} ({suffix})";
            var path = Path.Combine(outputFolder, $"{name}.{extension}");
            try
            {
                // Reserve atomically so concurrent conversions cannot select the same name.
                using var reservation = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                return path;
            }
            catch (IOException) when (File.Exists(path) || Directory.Exists(path))
            {
                // Keep existing results and try the next name.
            }
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

        if (item.CompressVideo && item.TargetSizeMb > 0 && ext is "mp4" or "mkv")
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

    public async Task<MediaOperationResult> LosslessTrimAsync(string inputFilePath, TimeSpan start, TimeSpan end, string outputFolder, CancellationToken ct = default)
    {
        if (!File.Exists(_dependencyService.FFmpegPath) || !File.Exists(inputFilePath))
            return MediaOperationResult.Failed("ไม่พบไฟล์ต้นทางหรือเอนจิน FFmpeg");

        string? outputFilePath = null;
        try
        {
            Directory.CreateDirectory(outputFolder);

            var baseName = Path.GetFileNameWithoutExtension(inputFilePath);
            var ext = Path.GetExtension(inputFilePath).TrimStart('.');
            outputFilePath = ReserveOutputPath(outputFolder, $"{baseName}_trimmed", ext);

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

            args.AddRange(new[] { "-c", "copy", "-avoid_negative_ts", "make_zero" });

            args.Add(outputFilePath);

            var result = await Cli.Wrap(_dependencyService.FFmpegPath)
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync(ct);

            if (result.ExitCode == 0 && new FileInfo(outputFilePath).Length > 0)
                return MediaOperationResult.Completed(outputFilePath);
            TryDeleteOutput(outputFilePath);
            return MediaOperationResult.Failed("FFmpeg ไม่สามารถตัดไฟล์นี้ได้");
        }
        catch (OperationCanceledException)
        {
            TryDeleteOutput(outputFilePath);
            return MediaOperationResult.Failed("ยกเลิกการตัดไฟล์");
        }
        catch (Exception ex)
        {
            TryDeleteOutput(outputFilePath);
            return MediaOperationResult.Failed(ex.Message);
        }
    }

    public async Task<AudioWaveformResult> AnalyzeAudioAsync(string inputFilePath, int peakCount = 900, CancellationToken ct = default)
    {
        if (!File.Exists(_dependencyService.FFmpegPath) || !File.Exists(inputFilePath))
            return new AudioWaveformResult { ErrorMessage = "ไม่พบไฟล์ต้นทางหรือเอนจิน FFmpeg" };

        var tempPath = Path.Combine(Path.GetTempPath(), $"mediastudio-waveform-{Guid.NewGuid():N}.pcm");
        var previewPath = Path.Combine(Path.GetTempPath(), $"mediastudio-preview-{Guid.NewGuid():N}.mp3");
        try
        {
            var result = await Cli.Wrap(_dependencyService.FFmpegPath)
                .WithArguments(new[] { "-y", "-i", inputFilePath,
                    "-map", "0:a:0", "-ac", "1", "-ar", "8000", "-f", "s16le", tempPath,
                    "-map", "0:a:0", "-vn", "-c:a", "libmp3lame", "-q:a", "5", previewPath })
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync(ct);
            if (result.ExitCode != 0 || !File.Exists(tempPath) || new FileInfo(tempPath).Length < 2 || !File.Exists(previewPath))
            {
                if (File.Exists(previewPath)) File.Delete(previewPath);
                return new AudioWaveformResult { ErrorMessage = "ไฟล์นี้ไม่มีแทร็กเสียงที่รองรับ" };
            }

            var duration = await GetDurationAsync(inputFilePath, ct);
            var totalSamples = new FileInfo(tempPath).Length / 2;
            var samplesPerPeak = Math.Max(1L, totalSamples / Math.Max(1, peakCount));
            var peaks = new List<double>(peakCount);
            await using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            var buffer = new byte[65536];
            long inBucket = 0;
            var maximum = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                for (var i = 0; i + 1 < read; i += 2)
                {
                    var value = Math.Abs((int)BitConverter.ToInt16(buffer, i));
                    if (value > maximum) maximum = value;
                    inBucket++;
                    if (inBucket < samplesPerPeak) continue;
                    peaks.Add(maximum / 32768d);
                    inBucket = 0;
                    maximum = 0;
                }
            }
            if (inBucket > 0) peaks.Add(maximum / 32768d);
            return new AudioWaveformResult { Success = true, Duration = duration, Peaks = peaks, PreviewPath = previewPath };
        }
        catch (OperationCanceledException) { if (File.Exists(previewPath)) File.Delete(previewPath); throw; }
        catch (Exception ex) { if (File.Exists(previewPath)) File.Delete(previewPath); return new AudioWaveformResult { ErrorMessage = ex.Message }; }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    public async Task<MediaOperationResult> TrimAudioAsync(string inputFilePath, TimeSpan start, TimeSpan end,
        string outputFolder, string outputFormat, double volumePercent = 100, CancellationToken ct = default)
    {
        if (!File.Exists(_dependencyService.FFmpegPath) || !File.Exists(inputFilePath))
            return MediaOperationResult.Failed("ไม่พบไฟล์ต้นทางหรือเอนจิน FFmpeg");
        if (end <= start) return MediaOperationResult.Failed("เวลาสิ้นสุดต้องมากกว่าเวลาเริ่มต้น");

        Directory.CreateDirectory(outputFolder);
        var ext = outputFormat.ToLowerInvariant();
        if (ext is not ("mp3" or "wav" or "flac")) ext = "mp3";
        volumePercent = Math.Clamp(volumePercent, 0, 200);
        var outputPath = ReserveOutputPath(outputFolder, $"{Path.GetFileNameWithoutExtension(inputFilePath)}_audio_trimmed", ext);
        try
        {
            var codecArgs = ext switch
            {
                "wav" => new[] { "-c:a", "pcm_s16le" },
                "flac" => new[] { "-c:a", "flac" },
                _ => new[] { "-c:a", "libmp3lame", "-q:a", "2" }
            };
            var args = new List<string>
            {
                "-y", "-ss", start.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture),
                "-t", (end - start).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture),
                "-i", inputFilePath, "-map", "0:a:0", "-vn"
            };
            if (Math.Abs(volumePercent - 100) > .001)
            {
                var factor = (volumePercent / 100d).ToString("0.####", CultureInfo.InvariantCulture);
                var filter = volumePercent > 100 ? $"volume={factor},alimiter=limit=0.95" : $"volume={factor}";
                args.AddRange(new[] { "-af", filter });
            }
            args.AddRange(codecArgs);
            args.Add(outputPath);
            var result = await Cli.Wrap(_dependencyService.FFmpegPath).WithArguments(args)
                .WithValidation(CommandResultValidation.None).ExecuteAsync(ct);
            if (result.ExitCode == 0 && new FileInfo(outputPath).Length > 0)
                return MediaOperationResult.Completed(outputPath);
            TryDeleteOutput(outputPath);
            return MediaOperationResult.Failed("ไม่สามารถตัดเสียงจากไฟล์นี้ได้");
        }
        catch (OperationCanceledException) { TryDeleteOutput(outputPath); return MediaOperationResult.Failed("ยกเลิกการตัดเสียง"); }
        catch (Exception ex) { TryDeleteOutput(outputPath); return MediaOperationResult.Failed(ex.Message); }
    }

    private static void TryDeleteOutput(string? path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
