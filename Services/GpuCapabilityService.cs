using System.IO;
using CliWrap;
using CliWrap.EventStream;
using MediaStudio.Models;

namespace MediaStudio.Services;

// On-demand only: no startup probe, background timer, network or settings writes.
public sealed class GpuCapabilityService
{
    private readonly Func<string, CancellationToken, Task<bool>> _probe;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (string Path, long Length, long Modified)? _identity;
    private EncoderCapability? _cached;
    private DateTimeOffset _expires;

    public GpuCapabilityService() : this(ProbeNvencAsync) { }
    public GpuCapabilityService(Func<string, CancellationToken, Task<bool>> probe) => _probe = probe;

    public async Task<EncoderCapability> CheckAsync(string path, bool refresh = false, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            (string Path, long Length, long Modified) identity;
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists) return new(EncoderAvailability.Unknown, "ไม่พบ FFmpeg กรุณาติดตั้งส่วนประกอบก่อนตรวจสอบ");
                identity = (file.FullName, file.Length, file.LastWriteTimeUtc.Ticks);
            }
            catch { return new(EncoderAvailability.Unknown, "อ่านข้อมูล FFmpeg ไม่ได้ จะใช้ CPU และตรวจสอบใหม่ได้"); }
            if (!refresh && _identity == identity && _cached != null && DateTimeOffset.UtcNow < _expires) return _cached;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            EncoderCapability result;
            try
            {
                result = await _probe(identity.Path, timeout.Token)
                    ? new(EncoderAvailability.Available, "ทดสอบ NVIDIA NVENC สำเร็จ")
                    : new(EncoderAvailability.Unavailable, "NVIDIA NVENC ยังใช้งานไม่ได้ จะใช้ CPU สำหรับงานที่ต้องเข้ารหัส");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { result = new(EncoderAvailability.Unknown, "ตรวจสอบ GPU เกินเวลา จะใช้ CPU และตรวจสอบใหม่ได้"); }
            catch (OperationCanceledException) { throw; }
            catch
            { result = new(EncoderAvailability.Unknown, "ตรวจสอบ GPU ไม่สำเร็จ จะใช้ CPU และตรวจสอบใหม่ได้"); }
            ct.ThrowIfCancellationRequested();
            _identity = identity;
            _cached = result;
            // Cache inconclusive results briefly as well, avoiding repeated expensive failed probes.
            _expires = DateTimeOffset.UtcNow.AddMinutes(result.Availability == EncoderAvailability.Unknown ? 1 : 5);
            return result;
        }
        finally { _gate.Release(); }
    }

    private static async Task<bool> ProbeNvencAsync(string path, CancellationToken ct)
    {
        var command = Cli.Wrap(path).WithArguments(new[]
        {
            "-hide_banner", "-nostdin", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=black:s=128x128:r=1",
            "-frames:v", "1", "-an", "-c:v", "h264_nvenc", "-f", "null", "-"
        }).WithValidation(CommandResultValidation.None);
        var exitCode = -1;
        await foreach (var item in command.ListenAsync(ct))
            if (item is ExitedCommandEvent exited) exitCode = exited.ExitCode;
        return exitCode == 0;
    }
}
