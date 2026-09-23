param([Parameter(Mandatory = $true)][string]$AppPath)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path $AppPath).Path
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("MediaStudio-consent-" + [Guid]::NewGuid().ToString('N'))
$appDirectory = Join-Path $root 'app'
$appData = Join-Path $root 'appdata'
New-Item -ItemType Directory -Path $appDirectory, $appData -Force | Out-Null
$target = Join-Path $appDirectory 'MediaStudio.exe'
Copy-Item -LiteralPath $source -Destination $target

$process = $null
try {
    $start = [System.Diagnostics.ProcessStartInfo]::new($target)
    $start.WorkingDirectory = $appDirectory
    $start.UseShellExecute = $false
    $start.Environment['MEDIASTUDIO_APPDATA_DIR'] = $appData
    $process = [System.Diagnostics.Process]::Start($start)
    $settingsPath = Join-Path $appData 'settings.json'
    for ($attempt = 0; $attempt -lt 30 -and !(Test-Path -LiteralPath $settingsPath); $attempt++) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) { throw "MediaStudio exited during first-run engine consent (exit code $($process.ExitCode))." }
    }
    if (!(Test-Path -LiteralPath $settingsPath)) { throw 'First-run consent state was not saved.' }
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    if (!$settings.HasSeenEnginePrompt) { throw 'First-run engine prompt was not reached.' }

    $engineDirectory = Join-Path $appDirectory 'bin'
    if (Test-Path -LiteralPath (Join-Path $engineDirectory 'ffmpeg.exe')) { throw 'FFmpeg downloaded before the user confirmed.' }
    if (Test-Path -LiteralPath (Join-Path $engineDirectory 'yt-dlp.exe')) { throw 'yt-dlp downloaded before the user confirmed.' }
    if (Get-ChildItem -LiteralPath $engineDirectory -Filter '*.partial' -ErrorAction SilentlyContinue) { throw 'An engine download started before consent.' }
    Write-Host 'PASS: first run reaches consent without starting an engine download.'
}
finally {
    if ($process -and !$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        try { if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force }; break }
        catch { Start-Sleep -Milliseconds 250 }
    }
}
