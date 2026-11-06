using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using MediaStudio.Models;
using MediaStudio.Services;

internal static class LocalDownloadRegression
{
    // Real yt-dlp against an ephemeral loopback-only server: no website accounts,
    // desktop automation, user media or externally reachable listener.
    public static async Task RunAsync(string ytDlp, string engine)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MediaStudio-local-download-" + Guid.NewGuid().ToString("N"))).FullName;
        var previousNoProxy = Environment.GetEnvironmentVariable("NO_PROXY");
        Environment.SetEnvironmentVariable("NO_PROXY", "127.0.0.1,localhost");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        using var stop = new CancellationTokenSource();
        Task? serving = null;
        try
        {
            var source = Path.Combine(root, "source.mp4");
            var start = new ProcessStartInfo(engine) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=1920x1080:r=5:d=0.4", "-f", "lavfi", "-i", "sine=frequency=440:duration=0.4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", "-movflags", "+faststart", source }) start.ArgumentList.Add(arg);
            using (var process = Process.Start(start)!)
            { var error = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); Check(process.ExitCode == 0, error); }
            var bytes = await File.ReadAllBytesAsync(source);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            serving = ServeAsync(listener, bytes, stop.Token);
            var dependency = new DependencyService();
            typeof(DependencyService).GetProperty(nameof(DependencyService.YtDlpPath))!.SetValue(dependency, Path.GetFullPath(ytDlp));
            typeof(DependencyService).GetProperty(nameof(DependencyService.FFmpegPath))!.SetValue(dependency, Path.GetFullPath(engine));
            var service = new YtDlpService(dependency);
            var url = $"http://127.0.0.1:{port}/video.mp4";
            var metadata = await service.GetMediaInfoAsync(url);
            Check(metadata != null, "Real yt-dlp metadata extraction failed");
            var item = new DownloadItem { Url = url, Quality = "720p", RequestedFileName = "ทดสอบดาวน์โหลด", DurationSeconds = .4 };
            var states = new List<TaskState>();
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DownloadItem.Status)) states.Add(item.Status); };
            var result = await service.DownloadAsync(item, root);
            Check(result.Success, item.StatusMessage);
            var output = await VideoInspectionService.InspectAsync(engine, result.OutputPath);
            Check(item.SourceDimensions == new VideoDimensions(1920,1080) && output.Dimensions == new VideoDimensions(1280,720) && output.HasAudio,
                "Real download/resize must preserve audio and expected dimensions");
            Check(states.Contains(TaskState.Converting) && states.Count(x => x == TaskState.Completed) == 1, "Do not complete before postprocessing");
            Check(!Directory.EnumerateDirectories(root, ".mediastudio-*").Any(), "Real downloader staging cleanup");
            Console.WriteLine("PASS: real yt-dlp loopback metadata/download/remux, Thai filename, FFmpeg 1080p-to-720p with audio and single completion; no GUI or external website used");
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_PROXY", previousNoProxy);
            stop.Cancel(); listener.Stop();
            if (serving != null) try { await serving; } catch (OperationCanceledException) { } catch (SocketException) { }
            Directory.Delete(root, true);
        }
    }

    private static async Task ServeAsync(TcpListener listener, byte[] bytes, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            var request = await reader.ReadLineAsync(ct) ?? "";
            string? line; string? range = null;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(ct)))
                if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) range = line[6..].Trim();
            var from = 0; var to = bytes.Length - 1;
            if (range != null && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                var parts = range[6..].Split('-');
                if (int.TryParse(parts[0], out var start)) from = Math.Clamp(start, 0, to);
                if (parts.Length > 1 && int.TryParse(parts[1], out var end)) to = Math.Clamp(end, from, to);
            }
            var header = $"HTTP/1.1 {(range == null ? "200 OK" : "206 Partial Content")}\r\nContent-Type: video/mp4\r\nContent-Length: {to-from+1}\r\nAccept-Ranges: bytes\r\nConnection: close\r\n"
                + (range == null ? "" : $"Content-Range: bytes {from}-{to}/{bytes.Length}\r\n") + "\r\n";
            try
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
                if (!request.StartsWith("HEAD ", StringComparison.Ordinal)) await stream.WriteAsync(bytes.AsMemory(from, to-from+1), ct);
            }
            catch (IOException) { /* yt-dlp can close a metadata probe early. */ }
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
