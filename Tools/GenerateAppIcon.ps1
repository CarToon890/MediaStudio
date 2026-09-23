param(
    [string]$SourcePath = (Join-Path $PSScriptRoot '..\Assets\MediaStudio.Cutout.png'),
    [string]$PngPath = (Join-Path $PSScriptRoot '..\Assets\MediaStudio.png'),
    [string]$IcoPath = (Join-Path $PSScriptRoot '..\Assets\MediaStudio.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$drawingAssemblies = @([System.Drawing.Bitmap].Assembly.Location, [System.Drawing.Rectangle].Assembly.Location)
Add-Type -ReferencedAssemblies $drawingAssemblies -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class IconAlphaCleaner
{
    public static void KeepCenterComponent(Bitmap bitmap)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = Math.Abs(data.Stride) * data.Height;
            var pixels = new byte[bytes];
            Marshal.Copy(data.Scan0, pixels, 0, bytes);
            var keep = new bool[bitmap.Width * bitmap.Height];
            var queue = new int[bitmap.Width * bitmap.Height];
            var queueHead = 0;
            var queueTail = 0;
            var center = (bitmap.Height / 2) * bitmap.Width + bitmap.Width / 2;
            queue[queueTail++] = center;
            keep[center] = true;
            while (queueHead < queueTail)
            {
                var index = queue[queueHead++];
                var x = index % bitmap.Width;
                var y = index / bitmap.Width;
                Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);
            }

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixelIndex = y * bitmap.Width + x;
                if (keep[pixelIndex]) continue;
                var offset = y * data.Stride + x * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = pixels[offset + 3] = 0;
            }
            Marshal.Copy(pixels, 0, data.Scan0, bytes);

            void Visit(int x, int y)
            {
                if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height) return;
                var pixelIndex = y * bitmap.Width + x;
                if (keep[pixelIndex]) return;
                var alpha = pixels[y * data.Stride + x * 4 + 3];
                if (alpha < 6) return;
                keep[pixelIndex] = true;
                queue[queueTail++] = pixelIndex;
            }
        }
        finally { bitmap.UnlockBits(data); }
    }
}
'@

function Resize-Image([System.Drawing.Image]$image, [int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($image, 0, 0, $size, $size)
    }
    finally { $graphics.Dispose() }
    return $bitmap
}

$source = [System.Drawing.Image]::FromFile((Resolve-Path $SourcePath))
$master = [System.Drawing.Bitmap]::new(1024, 1024, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($master)
try {
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($source, 0, 0, 1024, 1024)
}
finally {
    $graphics.Dispose()
    $source.Dispose()
}

[IconAlphaCleaner]::KeepCenterComponent($master)

$master.Save($PngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $resized = Resize-Image $master $size
    $stream = [System.IO.MemoryStream]::new()
    try {
        $resized.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames.Add($stream.ToArray())
    }
    finally { $stream.Dispose(); $resized.Dispose() }
}
$master.Dispose()

$file = [System.IO.File]::Open($IcoPath, [System.IO.FileMode]::Create)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size = $sizes[$i]
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
}
finally { $writer.Dispose(); $file.Dispose() }

Write-Host "Generated $PngPath and $IcoPath"
