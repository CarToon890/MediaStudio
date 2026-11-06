# GPU/device matrix

Environment observed 2026-11-06: Windows NT 10.0.26300.0, .NET SDK 9.0.203,
FFmpeg N-128448-g0f3d20ebf6-20261105. Baseline 6d99f43 plus current working diff.
Synthetic 64x64 H.264/AAC input; isolated temporary output/settings.

Automated evidence:
- GPU-preference real conversion: PASS, output encoder libx264, nonempty output.
  This records CPU completion; it does not identify the physical GPU/driver.
- Injected NVENC success / unavailable / timeout / exception / cancellation: PASS.
- GPU-off and non-NVENC formats skip probe; repeated unavailable result cached: PASS.
- FFmpeg identity change and explicit refresh invalidate cached results: PASS.
- GPU failure then CPU success; cancellation without retry: PASS.
- No startup probe, manual busy guard, preference preserved: PASS.

Device evidence still required:
- Usable NVIDIA NVENC with driver version: NOT RUN.
- Intel/AMD-only and deliberately unavailable NVIDIA driver: NOT RUN.
- Driver replacement while app remains open, refresh button: NOT RUN.
- Battery energy per task and idle draw: NOT RUN.

The cache expires after five minutes (inconclusive result after one minute).
Refresh explicitly after a driver/device change. Probe output uses the null muxer,
so no proxy/temp media is written. UI screenshots are under ignored artifacts/.
