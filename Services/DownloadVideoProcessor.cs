using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.EventStream;
using MediaStudio.Models;

namespace MediaStudio.Services;

public sealed class DownloadVideoProcessor(DependencyService dependency, GpuCapabilityService gpu)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<string> ProcessAsync(string source, DownloadItem item, string stage, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var info = await VideoInspectionService.InspectAsync(dependency.FFmpegPath, source, ct);
            var dimensions = info.Dimensions ?? throw new InvalidDataException("ไม่สามารถตรวจความละเอียดวิดีโอได้");
            var pixels = info.PixelDimensions ?? dimensions;
            var sourceDisplay = pixels == dimensions ? pixels.Display : $"{pixels.Display} (แสดงผล {dimensions.Display})";
            item.SourceDimensions = pixels;
            var target = dimensions.Fit(VideoDimensions.TargetHeight(item.Quality));
            item.OutputDimensions = pixels;
            if (target == dimensions)
            {
                item.ResolutionSummary = $"เลือก {item.Quality} • ไฟล์ต้นทาง {sourceDisplay} → บันทึกขนาดเดิม ไม่ได้อัปสเกล";
                return source;
            }
            if (info.IsHdr) throw new NotSupportedException("ยังไม่รองรับการลดขนาด HDR");
            item.Status = TaskState.Converting;
            item.ResolutionSummary = $"เลือก {item.Quality} • ไฟล์ต้นทาง {sourceDisplay} → กำลังลดเป็น {target.Display}";
            item.StatusMessage = "กำลังลดความละเอียด...";
            item.Progress = 0;
            var hardware = item.UseHardwareAcceleration &&
                (await gpu.CheckAsync(dependency.FFmpegPath, ct: ct)).Availability == EncoderAvailability.Available;
            var output = Path.Combine(stage, "resized.mp4");
            var succeeded = await EncodeAsync(source, output, target, item, hardware, ct);
            ct.ThrowIfCancellationRequested();
            if (!succeeded && hardware) succeeded = await EncodeAsync(source, output, target, item, false, ct);
            ct.ThrowIfCancellationRequested();
            if (!succeeded || !File.Exists(output) || new FileInfo(output).Length == 0) throw new IOException("ลดความละเอียดไม่สำเร็จ");
            var inspected = await VideoInspectionService.InspectAsync(dependency.FFmpegPath, output, ct);
            if (inspected.Dimensions != target || (info.HasAudio && !inspected.HasAudio)) throw new InvalidDataException("ผลลัพธ์ไม่ตรงตามที่เลือก");
            item.OutputDimensions = target;
            item.ResolutionSummary = $"เลือก {item.Quality} • ไฟล์ต้นทาง {sourceDisplay} → ลดเป็น {target.Display}";
            return output;
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> EncodeAsync(string source, string output, VideoDimensions target, DownloadItem item, bool hardware, CancellationToken ct)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", source, "-map", "0:V:0", "-map", "0:a:0?",
            "-vf", $"scale={target.Width}:{target.Height}:flags=lanczos,setsar=1", "-c:v", hardware ? "h264_nvenc" : "libx264" };
        args.AddRange(hardware ? new[] { "-preset", "p4", "-cq", "23" } : new[] { "-preset", "fast", "-crf", "22" });
        args.AddRange(new[] { "-pix_fmt", "yuv420p", "-c:a", "aac", "-movflags", "+faststart", "-metadata:s:v:0", "rotate=0", output });
        var exit = -1;
        await foreach (var message in Cli.Wrap(dependency.FFmpegPath).WithArguments(args).WithValidation(CommandResultValidation.None).ListenAsync(ct))
        {
            if (message is ExitedCommandEvent ended) exit = ended.ExitCode;
            if (message is StandardErrorCommandEvent line && item.DurationSeconds > 0)
            {
                var match = Regex.Match(line.Text, @"time=(\d+):(\d+):(\d+\.\d+)");
                if (!match.Success) continue;
                var seconds = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
                    + double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                item.Progress = Math.Clamp(seconds / item.DurationSeconds * 100, 0, 99);
                item.StatusMessage = $"กำลังลดความละเอียด: {item.Progress:F0}%";
            }
        }
        return exit == 0;
    }
}
