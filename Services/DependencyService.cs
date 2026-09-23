using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MediaStudio.Services;

public enum EngineSetupMode
{
    InstallMissing,
    RefreshAll
}

public class DependencyService
{
    private readonly string _binDirectory;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public string YtDlpPath { get; private set; } = string.Empty;
    public string FFmpegPath { get; private set; } = string.Empty;
    public bool IsReady => File.Exists(YtDlpPath) && File.Exists(FFmpegPath);

    public event Action<string, double>? DownloadProgressChanged;
    public event Action? AvailabilityChanged;

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

    public async Task<bool> EnsureDependenciesAsync(IProgress<string>? progress = null, CancellationToken ct = default,
        EngineSetupMode mode = EngineSetupMode.InstallMissing)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            if (mode == EngineSetupMode.RefreshAll)
                return await RefreshAllAsync(progress, ct);

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
                            var partialFf = targetFf + "." + Guid.NewGuid().ToString("N") + ".partial";
                            try
                            {
                                entry.ExtractToFile(partialFf);
                                ct.ThrowIfCancellationRequested();
                                File.Move(partialFf, targetFf, true);
                            }
                            finally
                            {
                                if (File.Exists(partialFf)) File.Delete(partialFf);
                            }
                            FFmpegPath = targetFf;
                            break;
                        }
                    }
                }

                if (File.Exists(tempZip)) File.Delete(tempZip);
            }

            progress?.Report("เอนจินพร้อมใช้งานเรียบร้อยแล้ว");
            AvailabilityChanged?.Invoke();
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

    private async Task<bool> RefreshAllAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var stagingDirectory = Path.Combine(_binDirectory, ".engine-update-" + Guid.NewGuid().ToString("N"));
        var stagedYtDlp = Path.Combine(stagingDirectory, "yt-dlp.exe");
        var stagedZip = Path.Combine(stagingDirectory, "ffmpeg.zip");
        var stagedFfmpeg = Path.Combine(stagingDirectory, "ffmpeg.exe");
        var localYtDlp = Path.Combine(_binDirectory, "yt-dlp.exe");
        var localFfmpeg = Path.Combine(_binDirectory, "ffmpeg.exe");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            progress?.Report("กำลังดาวน์โหลด yt-dlp รุ่นล่าสุด...");
            await DownloadFileAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", stagedYtDlp, "yt-dlp", ct);
            progress?.Report("กำลังดาวน์โหลด FFmpeg รุ่นล่าสุด...");
            await DownloadFileAsync("https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip", stagedZip, "FFmpeg", ct);
            progress?.Report("กำลังตรวจสอบไฟล์เอนจิน...");
            using (var archive = ZipFile.OpenRead(stagedZip))
            {
                var entry = archive.Entries.FirstOrDefault(x => x.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("ไม่พบ ffmpeg.exe ในไฟล์ที่ดาวน์โหลด");
                entry.ExtractToFile(stagedFfmpeg);
            }

            if (!await ValidateExecutableAsync(stagedYtDlp, "--version", ct) ||
                !await ValidateExecutableAsync(stagedFfmpeg, "-version", ct))
                throw new InvalidDataException("ไฟล์เอนจินที่ดาวน์โหลดไม่สามารถเริ่มทำงานได้");

            ReplaceEnginesAtomically(stagedYtDlp, stagedFfmpeg, localYtDlp, localFfmpeg);
            YtDlpPath = localYtDlp;
            FFmpegPath = localFfmpeg;
            progress?.Report("อัปเดตเอนจินเรียบร้อยแล้ว");
            AvailabilityChanged?.Invoke();
            return true;
        }
        finally
        {
            try { if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, true); }
            catch { }
        }
    }

    private static async Task<bool> ValidateExecutableAsync(string path, string argument, CancellationToken ct)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, argument)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try { await process.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(true); } catch { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void ReplaceEnginesAtomically(string stagedYtDlp, string stagedFfmpeg, string localYtDlp, string localFfmpeg)
    {
        var backupId = Guid.NewGuid().ToString("N");
        var backupYtDlp = localYtDlp + ".backup." + backupId;
        var backupFfmpeg = localFfmpeg + ".backup." + backupId;
        try
        {
            if (File.Exists(localYtDlp)) File.Move(localYtDlp, backupYtDlp);
            if (File.Exists(localFfmpeg)) File.Move(localFfmpeg, backupFfmpeg);
            File.Move(stagedYtDlp, localYtDlp);
            File.Move(stagedFfmpeg, localFfmpeg);
            TryDelete(backupYtDlp);
            TryDelete(backupFfmpeg);
        }
        catch
        {
            TryDelete(localYtDlp);
            TryDelete(localFfmpeg);
            if (File.Exists(backupYtDlp)) File.Move(backupYtDlp, localYtDlp);
            if (File.Exists(backupFfmpeg)) File.Move(backupFfmpeg, localFfmpeg);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private async Task DownloadFileAsync(string url, string destinationPath, string name, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        var partialPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await using (var fileStream = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
            {
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

                if (totalBytes >= 0 && totalRead != totalBytes)
                    throw new IOException("ดาวน์โหลดไฟล์เอนจินไม่ครบ");
            }

            ct.ThrowIfCancellationRequested();
            File.Move(partialPath, destinationPath, true);
        }
        finally
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
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
