param(
    [Parameter(Mandatory = $true)]
    [string]$AppPath,
    [Parameter(Mandatory = $true)]
    [string]$FFmpegPath
)

$resolvedApp = (Resolve-Path -LiteralPath $AppPath).Path
$resolvedFfmpeg = (Resolve-Path -LiteralPath $FFmpegPath).Path
$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("MediaStudio-startup-" + [Guid]::NewGuid().ToString("N"))
$process = $null

try {
    New-Item -ItemType Directory -Path $smokeRoot | Out-Null
    $testExe = Join-Path $smokeRoot "MediaStudio.exe"
    $sourceDirectory = Split-Path $resolvedApp
    foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -File) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $smokeRoot $file.Name)
    }

    $engineRoot = Join-Path $smokeRoot "bin"
    New-Item -ItemType Directory -Path $engineRoot | Out-Null
    Copy-Item -LiteralPath $resolvedFfmpeg -Destination (Join-Path $engineRoot "ffmpeg.exe")
    [System.IO.File]::WriteAllBytes((Join-Path $engineRoot "yt-dlp.exe"), [byte[]](0))

    $process = Start-Process -FilePath $testExe -WorkingDirectory $smokeRoot -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 3
    $process.Refresh()
    if ($process.HasExited) {
        throw "MediaStudio exited during startup with code $($process.ExitCode)."
    }
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
        throw "MediaStudio process is running but did not create its main window."
    }

    Write-Output "PASS: MediaStudio remained running after startup."
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
    if ($null -ne $process) {
        $process.Dispose()
        $process = $null
    }
    if (Test-Path -LiteralPath $smokeRoot) {
        $resolvedSmokeRoot = (Resolve-Path -LiteralPath $smokeRoot).Path
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not $resolvedSmokeRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a startup-test directory outside Temp."
        }
        for ($attempt = 0; $attempt -lt 5; $attempt++) {
            try {
                Remove-Item -LiteralPath $resolvedSmokeRoot -Recurse -Force -ErrorAction Stop
                break
            }
            catch {
                if ($attempt -eq 4) { throw }
                Start-Sleep -Milliseconds 250
            }
        }
    }
}
