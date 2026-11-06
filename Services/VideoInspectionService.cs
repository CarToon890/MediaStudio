using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.EventStream;
using MediaStudio.Models;

namespace MediaStudio.Services;

public static class VideoInspectionService
{
    // Read one decoded frame after FFmpeg's autorotation, not a website's selected
    // format estimate. Disabling checksums avoids unnecessary full-frame hashing.
    public static async Task<VideoInspection> InspectAsync(string engine, string path, CancellationToken ct = default)
    {
        var log = new StringBuilder();
        var command = Cli.Wrap(engine).WithArguments(new[] { "-hide_banner", "-nostdin", "-i", path,
            "-map", "0:V:0?", "-an", "-frames:v", "1", "-vf", "showinfo=checksum=0", "-f", "null", "-" })
            .WithValidation(CommandResultValidation.None);
        await foreach (var item in command.ListenAsync(ct))
            if (item is StandardErrorCommandEvent error && log.Length < 262144) log.AppendLine(error.Text);
        var text = log.ToString();
        var frame = Regex.Match(text, @"\[.*showinfo.*\].*\bs:(\d+)x(\d+).*", RegexOptions.IgnoreCase);
        VideoDimensions? dimensions = null;
        VideoDimensions? pixels = null;
        if (frame.Success && int.TryParse(frame.Groups[1].Value, out var width) && int.TryParse(frame.Groups[2].Value, out var height) && width > 0 && height > 0)
        {
            pixels = new(width, height);
            var sar = Regex.Match(frame.Value, @"\bsar:(\d+)/(\d+)");
            if (sar.Success && double.TryParse(sar.Groups[1].Value, CultureInfo.InvariantCulture, out var numerator)
                && double.TryParse(sar.Groups[2].Value, CultureInfo.InvariantCulture, out var denominator) && numerator > 0 && denominator > 0)
                width = checked((int)Math.Round(width * numerator / denominator));
            if (width > 0 && height > 0 && width <= 65536 && height <= 65536) dimensions = new(width, height);
        }
        var audio = Regex.Match(text, @"Stream #.*Audio:\s*([^,\s]+)");
        return new(dimensions, audio.Success, Regex.IsMatch(text, @"color_trc:(smpte2084|arib-std-b67)\b", RegexOptions.IgnoreCase), audio.Success ? audio.Groups[1].Value : null, pixels);
    }
}
