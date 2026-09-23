# Regression checks

Requires Windows, .NET 9 SDK and an installed FFmpeg with libx264, AAC, MP3 and FLAC encoders.

Run from the repository root:

```powershell
dotnet run --project Tests/MediaStudio.RegressionTests.csproj -c Release -- "C:\path\to\ffmpeg.exe"
```

These checks use synthetic media and a fake HTTP handler. They cover queue changes during conversion, cancellation, output collisions, Windows file-name validation, all supported conversion formats, waveform generation, videos without audio, MP3/WAV/FLAC trimming, workspace persistence and routing, and interrupted engine downloads. They do not access the network or the user's saved settings. Temporary media is deleted after the run.
