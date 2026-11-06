# v1.1.2 implementation progress

Source plan: user-provided MediaStudio_v1.1.2_Implementation_Plan.md.
Inspected baseline: 6d99f43cfe6ea35ccccbe7ec82722d089166c246.
No user settings, tags, remote branches or release configuration were changed.

## Review decisions

The sequence is sound: preserve operation ownership, add normalized states before
an observational Job Center, and tokenize before introducing another theme.
Treat the document's handoff as proposed workflow, not a separate authorization.

Clarifications required in implementation, resolved here for the foundation:

- Separate simulated UI evidence from physical-device release sign-off. Rendering
  smaller dimensions does not establish per-monitor DPI correctness.
- Keep Dark as the backward-compatible theme default when MS-11 is implemented.
- Keep MediaElement initially; native playback failure must not revoke FFmpeg
  processing readiness. Proxy generation and alternative players remain optional.
- Keep Job Center session-only and observational. Video trimming currently lacks
  its own cancellation command; isolated cancellation will require an owner CTS,
  not merely an adapter over a nonexistent command.
- NVENC discovery must not imply driver readiness. CPU fallback must preserve
  cancellation and output cleanup; never retry cancellation as a GPU failure.
- Keep target-size encoding approximate; two-pass encoding is optional scope.
- Preserve the current target framework in this work; runtime migration is separate.

## Implemented foundation

- PR-00: baseline Release build and complete regression suite executed; baseline
  captures retained locally at artifacts/ui-baseline-6d99f43. Added a read-only,
  non-publishing pull-request/manual CI workflow and manual evidence template.
- PR-01: extracted existing typography and surface spacing/radius into resource
  dictionaries, consumed by the existing shared styles. Dark values preserved.
  Semantic brushes continue to come from WPF-UI. Full component migration remains.
- PR-02: lower minimum constraints when the monitor work area is smaller; refit on
  DPI/work-area changes and unsubscribe display events on close. Audio controls
  wrap. Queue item widths follow the scroll viewport after resizing and recycling.
  Added breakpoint captures, smaller work areas, last-item scrolling, and an audio
  save-button reachability assertion. Physical multi-monitor checks remain pending.

## Remaining scope

PR-03 through PR-10 / MS-02 through MS-05 and MS-07 through MS-11 have not been
implemented by this foundation patch. MS-06 migration and MS-01/MS-12 device gates
are incomplete. This patch is not a claim that the entire twelve-item plan or
release gate is complete.

## Executed baseline evidence

- dotnet build MediaStudio.csproj -c Release --no-restore: PASS, zero warnings/errors.
- Complete regression runner with artifacts/test-tools/ffmpeg.exe: PASS, including
  six formats, queue mutation/cancel, collisions, waveform/audio trim, persistence,
  transfers, checksum/rollback and interrupted engine downloads.
- Original UI matrix: PASS, seven pages, four sizes, empty/content and 50 jobs.
- IconSmoke.ps1: PASS.
- NuGet vulnerability metadata was unavailable during restore (NU1900). This does
  not constitute a successful package-vulnerability audit.

Changed-build verification is recorded below after the final run. Manual checks
are NOT RUN until evidence is added to Tests/Manual.

## Final changed-build evidence (2026-11-06, Windows runner)

- Release build: PASS; resource dictionaries and view markup compile.
- Expanded UI runner: PASS, exit 0, seven pages x 14 requested logical sizes x
  empty/content states; exact capture dimensions asserted; 50-item queues scrolled
  to the final item; audio save reachable at 780 x 406 DIP. Inspected narrow audio
  controls and queue screenshots. Captures: artifacts/ui-v1.1.2.
- Portable publish: PASS with the existing win-x64 profile; output:
  artifacts/verify-plan-foundation/MediaStudio.exe.
- StartupSmoke.ps1: PASS (published process remains running).
- EngineConsentSmoke.ps1: PASS (consent reached without engine download).
- IconSmoke.ps1 and git diff --check: PASS.
- CI workflow added but not executed on GitHub in this session.
- Physical DPI, unplugging monitors, NVDA, hardware encoders, live websites and
  portable old-to-new upgrade: NOT RUN in this patch. Existing baseline automated
  rollback checks passed; that is not a substitute for a portable upgrade smoke.

The first publish attempt with --no-restore failed NETSDK1047 (missing win-x64
assets). Publishing with restore succeeded from installed package caches. The
publish command disabled the unavailable network vulnerability audit for that
invocation only; no project audit setting was changed.

## Preview efficiency follow-up (2026-11-06)

User priority: reduce unnecessary energy use and improve device compatibility.
Implemented work avoidance rather than changing codec quality or forcing a GPU:

- Coalesce synchronous waveform peak/resize bursts on the render dispatcher;
  defer hidden/minimized waveform redraws until visible.
- Move retained playhead visuals instead of reconstructing selection overlays on
  each playback tick; share a frozen waveform brush.
- Pause previews and stop position timers when hidden or minimized. Audio releases
  its output/reader; playback resumes only through explicit Play.
- Open the audio device lazily, preserve keyboard seek before device acquisition,
  and keep trim readiness on device-initialization failure.
- Reuse the video position timer and stop it on playback failure/state reset.
- Add PreviewEfficiencyRegression and --preview-only for lightweight validation.

Evidence:
- Release build: PASS, zero warnings/errors in the application build.
- Full regression suite: PASS, including six formats, cancellation, output
  collisions, persistence, trim, update rollback and the 14-size UI matrix.
- Preview lifecycle suite: PASS. Final keyboard-seek/lazy-device change validated
  with --preview-only against a fresh isolated build at artifacts/preview-final.
- Final portable publish: PASS at artifacts/verify-power-compatibility.
- Published StartupSmoke and EngineConsentSmoke: PASS.
- IconSmoke and git diff --check: PASS.
- Rebuilding the usual test output while its DLL was held open failed MSB3027;
  isolated output build and execution succeeded. No unrelated process was killed.
- NuGet vulnerability metadata remained unavailable (NU1900) for the regression
  project; no successful vulnerability audit is implied.

Hardware-independent timer/state tests do not measure watts or battery life, nor
prove real codec/audio-driver compatibility. Physical battery, integrated/NVIDIA
GPU, headset switching, mixed-DPI monitors and ARM emulation remain NOT RUN;
see Tests/Manual/README.md. Target remains the existing Windows x64 WPF package.

Final runner cleanup correction:
The old two-argument CLI branch returned before the runner's existing final
Environment.Exit(0), allowing WPF/native foreground threads to keep a UI-only
process alive after assertions passed. Both modes now flow through temporary
folder cleanup and the existing shared exit path. Two test processes launched
in this session were stopped with Ctrl+C after their assertions had completed.

Fresh final verification:
- dotnet build Tests/MediaStudio.RegressionTests.csproj -c Release --no-restore
  -o artifacts/preview-final-exit: PASS (NU1900 metadata warning only).
- dotnet artifacts/preview-final-exit/MediaStudio.RegressionTests.dll
  artifacts/test-tools/ffmpeg.exe --preview-only: PASS, process exit code 0.
This supersedes the earlier preview-only assertion output that preceded process
exit. Production app behavior is not affected by the test CLI exit correction.

## MS-08 follow-up: on-demand GPU readiness

Implemented the GPU/device compatibility increment:
- Independent Unknown/Available/Unavailable capability; preference remains saved
  unchanged. Settings provides an explicit refresh, with a busy guard.
- One-frame synthetic NVENC probe only for eligible requested video encodes or
  explicit user action. No startup/background/network probe. Results cached by
  FFmpeg path/length/modification time for five minutes; unknown for one minute.
- CPU path for unavailable/inconclusive probes. Actual GPU encode failure retries
  CPU once; cancellation does not retry. Non-NVENC jobs do not probe or retry GPU.
- Successful conversions expose UsedEncoder and persistent EncoderNotice; zero-byte
  outputs are rejected and reserved outputs are cleaned on failure/cancellation.
- Engine replacement/app update cannot start from Settings during a manual GPU check.

Executed evidence (Windows NT 10.0.26300.0, SDK 9.0.203,
FFmpeg N-128448-g0f3d20ebf6-20261105):
- Isolated build artifacts/gpu-final: PASS; regression project NU1900 warning only.
- Full regression runner: PASS, exit 0, including new injected capability/fallback
  cases and real GPU-preference conversion (actual successful encoder libx264).
- Seven-page 14-size empty/populated UI matrix and existing queue, cancellation,
  audio/trim, persistence and update rollback checks: PASS.
- No startup probe, manual busy guard and unchanged GPU preference: PASS.
- Final targeted UI runner artifacts/gpu-ui-final --preview-only: PASS, exit 0;
  Settings GPU button is reachable after scrolling at 780x406 DIP. Inspected
  GPU-settings-result.png. Fixed VisualBrush viewbox to prevent offscreen scroll
  content from shrinking the captured UI; final targeted screenshot verified.
- Portable publish artifacts/verify-gpu-compatibility: PASS.
- Published StartupSmoke, EngineConsentSmoke, IconSmoke, git diff --check: PASS.

Probe flags follow FFmpeg's official null-muxer documentation:
https://www.ffmpeg.org/ffmpeg-formats.html#null

Real NVIDIA success, driver replacement, multiple physical devices, battery energy
and physical mixed-DPI checks remain NOT RUN; see Tests/Manual/GPU_Matrix.md.
MS-08 code is implemented with automated evidence; its physical release gates and
other remaining plan workstreams are not declared complete by this increment.

## Latest round: download and safety improvements

The newest implementation/evidence record supersedes prior runtime-blocked notes:
[IMPROVEMENT_ROUND_2026-11-06.md](IMPROVEMENT_ROUND_2026-11-06.md).
The project now targets .NET 10; headless tests and real yt-dlp loopback integration
passed. Upscaling remains deferred. No computer-control tools were used.
