# Regression checks

Requires Windows, .NET 10 SDK (10.0.403; see global.json) and an installed FFmpeg with libx264, AAC, MP3 and FLAC encoders.

Run from the repository root:

```powershell
dotnet run --project Tests/MediaStudio.RegressionTests.csproj -c Release -- "C:\path\to\ffmpeg.exe"
```

These checks use synthetic media and a fake HTTP handler. They cover queue changes during conversion, cancellation, output collisions, Windows file-name validation, all supported conversion formats, waveform generation, videos without audio, MP3/WAV/FLAC trimming, workspace persistence and routing, and interrupted engine downloads. They do not access the network or the user's saved settings. Temporary media is deleted after the run.

The release workflow also runs `StartupSmoke.ps1` against the published single-file executable. It opens the real WPF application with isolated temporary engines and fails when the process exits during startup, including XAML resource and dependency-injection errors.

`IconSmoke.ps1` verifies the transparent PNG and the seven embedded ICO sizes used by Explorer, the title bar, Taskbar and Alt+Tab.

`EngineConsentSmoke.ps1` opens an isolated first-run copy and verifies that the confirmation flow is reached without downloading either engine before the user accepts.


`PatchRegression.cs` adds v1.1.1 coverage for invalid video duration, trim folder failures/cancellation/retry, both audio load completion orders, queue CanExecute notifications, blank URL feedback, corrupt history preservation and locked-file save recovery. Audio races use controlled completion rather than timing sleeps.

Test a real portable upgrade locally against the existing v1.1.0 executable (no network):

```powershell
./Tests/PortableUpdateSmoke.ps1 -OldAppPath artifacts/publish-final/MediaStudio.exe -NewAppPath artifacts/publish-v1.1.1/MediaStudio.exe -FFmpegPath "C:\path\to\ffmpeg.exe"
```

This uses isolated app data and executable copies. The actual 1.1.0 parent process is held at the initial Windows debugger breakpoint and terminated after updater validation, because that version does not isolate workspace history. This initializes the module list required for executable identity checks without running the old application entry point. The default helper is copied from 1.1.0, matching the shipped update path; pass `-UpdaterAppPath` to also check a different helper version. It exercises replacement/restart/health-marker validation, and cleans up only its test processes and directories. It does not publish a release, check GitHub, or automate the old Settings update button; parent shutdown is controlled by the test. The existing regression checks cover release parsing, download/checksum validation and rollback separately.

Use the v1.1.0 build containing `--apply-update` support (the v1.1.0 tag includes it). The older local `publish-v1.1.0` artifact predates that feature despite its version number, so it is not a valid in-app-updater baseline.


## v1.1.2 UX checks

`UxRegression.cs` checks shared output notifications, persisted folder changes, failed saves, destination snapshots, both metadata completion orders and download readiness. `UiRegression.cs` loads the same resource dictionary and actual views on an isolated STA dispatcher. It captures empty/content states for all seven pages at notebook, 150%, 200% and minimum-window logical sizes, using 50-item queues, real audio/video analysis and real command bindings, including the detached transfer popup.

```powershell
dotnet run --project Tests/MediaStudio.RegressionTests.csproj -c Release -- "C:\path\to\ffmpeg.exe" --ui-only
./Tests/PortableUpdateSmoke.ps1 -OldAppPath artifacts/publish-v1.1.1/MediaStudio.exe -NewAppPath artifacts/publish-v1.1.2/MediaStudio.exe -FFmpegPath "C:\path\to\ffmpeg.exe"
```

UI captures are written to `artifacts/ui-v1.1.2`. Display scaling is simulated in the WPF renderer; the harness does not change OS scaling or prove physical multi-monitor behavior. Hardware NVENC, real website downloads and screen readers remain device/manual checks. Portable upgrade derives both versions from the provided executables; the old helper must support in-app update mode.

## Implementation foundation checks

The expanded UI harness renders 14 work-area/breakpoint configurations, including
910 x 472 and 780 x 406 DIP. It checks the first and last queue items after resize,
and scrolls the narrow audio editor to verify the save action is reachable.
Baseline images are retained locally in `artifacts/ui-baseline-6d99f43`; changed
captures remain in `artifacts/ui-v1.1.2`. Both are ignored artifacts.

Physical evidence and pending checks: [Manual/README.md](Manual/README.md).

`PreviewEfficiencyRegression.cs` runs inside the isolated UI dispatcher. It checks
that peak batches defer rebuilding, playhead updates preserve overlay identities,
idle/import do not acquire an audio device, tab changes/minimizing stop preview
timers, restoration does not auto-play, and trim readiness survives suspension.
Timer cleanup tests start only the UI timers and state flags, so they do not
require installed codecs, speakers, or a working physical audio output device.

For a lightweight preview lifecycle check without generating the layout matrix:

```powershell
dotnet run --project Tests/MediaStudio.RegressionTests.csproj -c Release -- "C:\path\to\ffmpeg.exe" --preview-only
```

This still uses synthetic media and the actual WPF views, but skips the 14-size
screenshot pass and unrelated encode/update scenarios. Use the full regression
runner when changing processing, cancellation or persistence.

## GPU regression checks

`GpuCapabilityRegression.cs` covers missing/changed engines, cache/refresh,
inconclusive/failed/cancelled probes, GPU-off, non-NVENC jobs, CPU fallback,
actual encoder reporting and cancellation without retry. It includes one real
synthetic conversion with the GPU preference enabled; GPU success is optional,
but a nonempty successful output is required. The full suite includes these checks.

```powershell
dotnet run --project Tests/MediaStudio.RegressionTests.csproj -c Release -- "C:\path\to\ffmpeg.exe" --gpu-only
```

Probe command uses a one-frame encode and the FFmpeg null muxer, documented in
[FFmpeg formats](https://www.ffmpeg.org/ffmpeg-formats.html#null).
Manual NVIDIA/driver/device evidence remains separate from injected cases.

## Download/safety regression modes

Use the SDK pinned in global.json (10.0.403). With the project-local SDK:

```powershell
./artifacts/dotnet10/dotnet.exe build Tests/MediaStudio.RegressionTests.csproj -c Release -o artifacts/checks
./artifacts/dotnet10/dotnet.exe artifacts/checks/MediaStudio.RegressionTests.dll artifacts/test-tools/ffmpeg.exe --safety-only
./artifacts/dotnet10/dotnet.exe artifacts/checks/MediaStudio.RegressionTests.dll artifacts/test-tools/ffmpeg.exe --core-only
./Tests/DependencyAudit.ps1 -Dotnet ./artifacts/dotnet10/dotnet.exe
```

--core-only runs processing/persistence/update regressions without creating a WPF
window, input automation, dialogs or desktop interaction. --safety-only runs the
new rollback locks, maintenance exclusion (including Home setup), integrity,
partial-file, metadata, real resize/no-upscale/collision/cancel, preview cleanup,
audio-only encoder and controlled child-process cancellation checks. Only the
owned test subprocess is terminated; the user's running application is untouched.
The no-argument-mode full regression still includes the optional WPF UI matrix.
Do not run startup/consent GUI smoke on the user's desktop without authorization.

The installer digest flow is covered with fake HTTP metadata and hash mismatch
fixtures. Live metadata was read without downloading or replacing user engines.
Engine digest verification uses GitHub's asset digest, not independent GPG trust.

Real yt-dlp integration without external sites or UI (requires an existing verified
engine path):

```powershell
./artifacts/dotnet10/dotnet.exe artifacts/checks/MediaStudio.RegressionTests.dll --download-local C:/path/to/yt-dlp.exe C:/path/to/ffmpeg.exe
```

This fixture serves synthetic MP4 on an ephemeral loopback-only port, exercises
real yt-dlp metadata/download/remux plus FFmpeg resize, and removes its test data.
It does not download personal media, play audio or open windows.
