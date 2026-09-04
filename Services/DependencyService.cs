using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MediaStudio.Services;

public class DependencyService
{
    private readonly string _binDirectory;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public string YtDlpPath { get; private set; } = string.Empty;
    public string FFmpegPath { get; private set; } = string.Empty;
    public bool IsReady => File.Exists(YtDlpPath) && File.Exists(FFmpegPath);

    public event Action<string, double>? DownloadProgressChanged;

    public DependencyService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MediaStudio/1.0 (Windows NT 10.0; Win64; x64)");

        _binDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin");
        Directory.CreateDirectory(_binDirectory);

        YtDlpPath = Path.Combine(_binDirectory, "yt-dlp.exe");
        FFmpegPath = Path.Combine(_binDirectory, "ffmpeg.exe");

        // Check if existing in PATH or local
        if (!File.Exists(YtDlpPath))
        {
            var pathYt = FindInPath("yt-dlp.exe");
            if (!string.IsNullOrEmpty(pathYt)) YtDlpPath = pathYt;
        }

        if (!File.Exists(FFmpegPath))
        {
            var pathFf = FindInPath("ffmpeg.exe");
            if (!string.IsNullOrEmpty(pathFf)) FFmpegPath = pathFf;
        }
    }

    public async Task<bool> EnsureDependenciesAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            // 1. Ensure yt-dlp
            if (!File.Exists(YtDlpPath))
            {
                progress?.Report("กำลังดาวน์โหลดเอนจิน yt-dlp...");
                var ytDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
                var targetPath = Path.Combine(_binDirectory, "yt-dlp.exe");
                await DownloadFileAsync(ytDlpUrl, targetPath, "yt-dlp", ct);
                YtDlpPath = targetPath;
            }

            // 2. Ensure ffmpeg
            if (!File.Exists(FFmpegPath))
            {
                progress?.Report("กำลังดาวน์โหลดเอนจิน FFmpeg...");
                var ffmpegZipUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
                var tempZip = Path.Combine(_binDirectory, "ffmpeg_temp.zip");
                await DownloadFileAsync(ffmpegZipUrl, tempZip, "FFmpeg", ct);

                progress?.Report("กำลังแตกไฟล์ FFmpeg...");
                using (var archive = ZipFile.OpenRead(tempZip))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            var targetFf = Path.Combine(_binDirectory, "ffmpeg.exe");
                            entry.ExtractToFile(targetFf, true);
                            FFmpegPath = targetFf;
                            break;
                        }
                    }
                }

                if (File.Exists(tempZip)) File.Delete(tempZip);
            }

            progress?.Report("เอนจินพร้อมใช้งานเรียบร้อยแล้ว");
            return IsReady;
        }
        catch (Exception ex)
        {
            progress?.Report($"เกิดข้อผิดพลาดในการโหลดเอนจิน: {ex.Message}");
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task DownloadFileAsync(string url, string destinationPath, string name, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        var totalRead = 0L;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
            totalRead += bytesRead;

            if (totalBytes > 0)
            {
                var percentage = (double)totalRead / totalBytes * 100;
                DownloadProgressChanged?.Invoke(name, percentage);
            }
        }
    }

    private static string? FindInPath(string filename)
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path.Trim(), filename);
            if (File.Exists(fullPath)) return fullPath;
        }
        return null;
    }
}
