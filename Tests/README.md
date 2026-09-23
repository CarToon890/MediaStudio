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
