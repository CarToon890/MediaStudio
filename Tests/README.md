# Regression checks

Requires Windows, .NET 9 SDK and an installed FFmpeg with libx264, AAC, MP3 and FLAC encoders.

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
