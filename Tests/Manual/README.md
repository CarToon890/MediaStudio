# Manual release evidence

Automated rendering simulates DPI; it cannot establish monitor, codec, GPU or
screen reader behavior. Do not mark these checks passed without device evidence.

For each run record: build SHA + dirty diff, Windows build, display resolution,
scale, work area, GPU/driver, FFmpeg version, expected/actual result, screenshot,
severity and owner. Use synthetic fixtures and isolated application data.

## Matrix (all pending device verification)

- DPI: 1366x768 at 125/150%; 1920x1080 at 100/150%; 2560x1440 at 200%.
- Unequal-DPI monitors: move, unplug, maximize/restore, snap, Alt+Tab, RDP.
- All seven pages: empty and populated; critical controls reachable by scrolling.
- Keyboard/NVDA: load audio, select range, volume 50/100/150%, preview, save.
- High Contrast and focus visibility.
- Codecs: H.264/AAC, VP9/Opus, HEVC, optional AV1, audio/video-only, corrupt file.
- GPU: no NVIDIA device; usable NVENC; unavailable driver; CPU fallback.
- Workflow: six output formats, compression, trim, transfers, retry/cancel,
  duplicate names, denied folder/disk full, offline/slow network.
- Distribution: first-run engine consent, engine update, portable upgrade,
  checksum rejection and rollback. Never create release tags for verification.

## Evidence template

Case ID:
Build / environment:
Steps / expected:
Actual / screenshot:
Result: NOT RUN | PASS | FAIL
Severity / owner / follow-up:

Release approval requires all P1 checks and triaged P2 regressions. Theme cases
must be added when Light/System mode is implemented.

## Power and device validation (pending physical evidence)

Use the same synthetic file, brightness, power plan, screen refresh rate, battery
state and network conditions for before/after comparisons. Allow startup and
media analysis to settle. Record idle/visible playback/hidden tab/minimized states
separately, and distinguish total energy per completed encode from instantaneous
CPU/GPU utilization. Repeat measurements; do not extrapolate battery life from a
single CPU sample. Record measurement tool, duration and environment with results.

- Low-power Windows x64 laptop with integrated GPU, on battery and AC.
- Windows x64 desktop with NVIDIA available, absent or driver unavailable.
- Audio device absent, unplugged, Bluetooth reconnect and device changed.
- Preview pause after tab switch/minimize; explicit Play resumes at the prior
  position; selection-end stop and volume still work.
- Verify conversions/trims/downloads continue while previews are suspended.
- Idle: preview timers stopped; no repeated waveform redraw; no silent playback.
- 125/150/175/200% DPI, small work areas, monitor changes and RDP.

All physical power/device cases above are NOT RUN. Automated timer/render checks
verify work avoidance, not electrical energy. ARM64 native packaging, Windows on
ARM emulation, Linux, macOS, phones and tablets are not validated by this win-x64
WPF build.
