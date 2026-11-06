namespace MediaStudio.Models;

public sealed record VideoDimensions(int Width, int Height)
{
    public string Display => $"{Width}×{Height}";
    public static int TargetHeight(string quality) => quality switch
    { "4K (2160p)" => 2160, "2K (1440p)" => 1440, "720p" => 720, "480p" => 480, _ => 1080 };
    public bool IsNativeFor(int height)
    {
        var longEdge = Math.Ceiling(height * 16d / 9 / 2) * 2;
        return Fit(height) == this && (Width >= Height
            ? Math.Abs(Height - height) <= 2 || Math.Abs(Width - longEdge) <= 2
            : Math.Abs(Width - height) <= 2 || Math.Abs(Height - longEdge) <= 2);
    }
    public VideoDimensions Fit(int height)
    {
        var landscape = Width >= Height;
        var longEdge = Math.Ceiling(height * 16d / 9 / 2) * 2;
        var maxWidth = landscape ? longEdge : height;
        var maxHeight = landscape ? height : longEdge;
        var factor = Math.Min(1, Math.Min(maxWidth / Width, maxHeight / Height));
        return factor >= 1 ? this : new(Math.Max(2, (int)Math.Round(Width * factor / 2, MidpointRounding.AwayFromZero) * 2), Math.Max(2, (int)Math.Round(Height * factor / 2, MidpointRounding.AwayFromZero) * 2));
    }
}

public sealed record VideoInspection(VideoDimensions? Dimensions, bool HasAudio, bool IsHdr, string? AudioCodec, VideoDimensions? PixelDimensions = null);
