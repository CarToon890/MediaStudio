using System.IO;
using MediaStudio.Models;
using MediaStudio.Services;

internal static class GpuCapabilityRegression
{
    public static async Task RunAsync(string root, string source, DependencyService dependency)
    {
        var engine = Path.Combine(root, "probe-engine.exe");
        File.WriteAllText(engine, "fixture");
        var calls = 0;
        var available = true;
        var gpu = new GpuCapabilityService((_, _) => { calls++; return Task.FromResult(available); });
        Check((await gpu.CheckAsync("")).Availability == EncoderAvailability.Unknown && calls == 0, "Missing engine must not launch a probe");
        Check((await gpu.CheckAsync(engine)).Availability == EncoderAvailability.Available, "Successful encode probe");
        await gpu.CheckAsync(engine);
        Check(calls == 1, "Repeated checks must use cache");
        available = false;
        Check((await gpu.CheckAsync(engine, refresh: true)).Availability == EncoderAvailability.Unavailable && calls == 2, "Explicit refresh");
        File.AppendAllText(engine, "engine update");
        await gpu.CheckAsync(engine);
        Check(calls == 3, "Engine identity change must invalidate cache");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => gpu.CheckAsync(engine)));
        Check(calls == 3, "Concurrent cached checks must not launch more probes");

        var timeout = new GpuCapabilityService((_, _) => throw new OperationCanceledException());
        Check((await timeout.CheckAsync(engine)).Availability == EncoderAvailability.Unknown, "Timeout is inconclusive, not available");
        var failedProbe = new GpuCapabilityService((_, _) => throw new IOException("driver unavailable"));
        Check((await failedProbe.CheckAsync(engine)).Availability == EncoderAvailability.Unknown, "Probe errors must fail soft");
        using var cancel = new CancellationTokenSource();
        var cancelled = new GpuCapabilityService((_, ct) => { cancel.Cancel(); ct.ThrowIfCancellationRequested(); return Task.FromResult(true); });
        try { await cancelled.CheckAsync(engine, ct: cancel.Token); throw new Exception("Cancellation swallowed"); }
        catch (OperationCanceledException) { }

        var probes = 0;
        var unavailable = new GpuCapabilityService((_, _) => { probes++; return Task.FromResult(false); });
        var fake = new ConversionRunner(dependency, unavailable);
        var folder = Directory.CreateDirectory(Path.Combine(root, "gpu-output")).FullName;
        var cpu = Item(source);
        Check((await fake.ConvertAsync(cpu, folder, false)).Success && probes == 0 && cpu.UsedEncoder == "libx264", "GPU-off must not probe");
        foreach (var format in new[] { "MP3", "WAV", "FLAC", "GIF", "MKV" })
        {
            var item = Item(source); item.TargetFormat = format;
            Check((await fake.ConvertAsync(item, folder, true)).Success && probes == 0, "Non-NVENC format must not probe");
        }
        for (var i = 0; i < 2; i++)
        {
            var item = Item(source);
            Check((await fake.ConvertAsync(item, folder, true)).Success && item.UsedEncoder == "libx264" && item.EncoderNotice.Length > 0, "Unavailable GPU must use CPU with explanation");
        }
        Check(probes == 1 && fake.Attempts.All(x => !x), "Unavailable GPU must not be retried per file");

        var ready = new GpuCapabilityService((_, _) => Task.FromResult(true));
        fake = new ConversionRunner(dependency, ready) { FailHardware = true };
        var fallback = Item(source);
        Check((await fake.ConvertAsync(fallback, folder, true)).Success && fake.Attempts.SequenceEqual(new[] { true, false })
            && fallback.UsedEncoder == "libx264" && fallback.EncoderNotice.Length > 0, "Encode failure must retry CPU once");
        fake = new ConversionRunner(dependency, ready);
        var hardware = Item(source);
        Check((await fake.ConvertAsync(hardware, folder, true)).Success && hardware.UsedEncoder == "h264_nvenc", "Actual successful GPU encoder reported");
        fake = new ConversionRunner(dependency, ready) { CancelHardware = true };
        var cancelledItem = Item(source);
        Check(!(await fake.ConvertAsync(cancelledItem, folder, true)).Success && cancelledItem.Status == TaskState.Cancelled
            && fake.Attempts.Count == 1 && string.IsNullOrEmpty(cancelledItem.UsedEncoder), "Cancellation must not retry CPU or report a successful encoder");

        // Real controlled encode probe: success is optional; a usable CPU output is mandatory.
        var real = new FFmpegService(dependency);
        var realItem = Item(source);
        Check((await real.ConvertAsync(realItem, folder, true)).Success && new FileInfo(realItem.OutputPath).Length > 0, "Real GPU preference/CPU fallback must produce output");
        Console.WriteLine($"PASS: GPU cache/refresh/engine change, missing/failed/cancelled probes, GPU-off, non-video skip, fallback and cancellation; real encoder={realItem.UsedEncoder}");
    }

    private static ConversionItem Item(string source) => new() { SourceFilePath = source, TargetFormat = "MP4", FileName = "clip.mp4" };
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class ConversionRunner(DependencyService dependency, GpuCapabilityService gpu) : FFmpegService(dependency, gpu)
    {
        public List<bool> Attempts { get; } = new();
        public bool FailHardware { get; init; }
        public bool CancelHardware { get; init; }
        public override Task<TimeSpan> GetDurationAsync(string path, CancellationToken ct = default) => Task.FromResult(TimeSpan.FromSeconds(1));
        protected override Task<(bool Success, string? Error)> ExecuteConversionAsync(ConversionItem item, string output, double seconds, bool hardware, CancellationToken ct)
        {
            Attempts.Add(hardware);
            if (hardware && CancelHardware) throw new OperationCanceledException();
            File.WriteAllText(output, "synthetic output");
            return Task.FromResult<(bool, string?)>(hardware && FailHardware ? (false, "fixture failure") : (true, null));
        }
    }
}
