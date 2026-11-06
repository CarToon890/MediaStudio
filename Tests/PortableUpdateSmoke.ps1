param(
    [Parameter(Mandatory = $true)][string]$OldAppPath,
    [Parameter(Mandatory = $true)][string]$NewAppPath,
    [Parameter(Mandatory = $true)][string]$FFmpegPath,
    [string]$UpdaterAppPath
)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('MediaStudio-upgrade-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path ([IO.Path]::GetTempPath()) ('MediaStudio/updates/smoke-' + [Guid]::NewGuid().ToString('N'))
$oldVersion = [Version][Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path -LiteralPath $OldAppPath).Path).FileVersion
$newVersion = [Version][Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path -LiteralPath $NewAppPath).Path).FileVersion
if ($newVersion -le $oldVersion) { throw 'New portable version must be greater than the installed baseline.' }
$old = $null
$helper = $null
try {
    $appDir = Join-Path $root 'app'
    $dataDir = Join-Path $root 'appdata'
    New-Item -ItemType Directory -Path $appDir, $dataDir, $stage | Out-Null
    $target = Join-Path $appDir 'MediaStudio.exe'
    $source = Join-Path $stage 'MediaStudio.exe'
    Copy-Item -LiteralPath $OldAppPath -Destination $target
    Copy-Item -LiteralPath $NewAppPath -Destination $source
    $engineDir = Join-Path $appDir 'bin'
    New-Item -ItemType Directory -Path $engineDir | Out-Null
    Copy-Item -LiteralPath $FFmpegPath -Destination (Join-Path $engineDir 'ffmpeg.exe')
    [IO.File]::WriteAllBytes((Join-Path $engineDir 'yt-dlp.exe'), [byte[]]@(0))
    $settings = @{ DownloadDirectory = (Join-Path $root 'output'); HasSeenEnginePrompt = $true; HasCompletedOnboarding = $true; LastVisitedTab = 'Home' }
    $settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataDir 'settings.json') -Encoding utf8
    # Hold the actual old executable at the initial debugger breakpoint, after
    # Windows initializes its module list but before the application entry point.
    # CREATE_SUSPENDED alone is too early for Process.MainModule validation.
    if (-not ('MediaStudioSmoke.DebugParent' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
namespace MediaStudioSmoke {
 public static class DebugParent {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  struct Startup { public int cb; public string reserved, desktop, title; public int x,y,w,h,cx,cy,fill,flags; public short show, reserved2; public IntPtr ptr, input, output, error; }
  [StructLayout(LayoutKind.Sequential)] struct Info { public IntPtr process, thread; public int pid, tid; }
  [StructLayout(LayoutKind.Explicit, Size=176)] struct DebugEvent {
   [FieldOffset(0)] public uint code; [FieldOffset(4)] public uint pid;
   [FieldOffset(8)] public uint tid; [FieldOffset(16)] public uint exception;
  }
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  static extern bool CreateProcess(string app, string command, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref Startup start, out Info info);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool WaitForDebugEvent(out DebugEvent e, uint timeout);
  [DllImport("kernel32.dll")] static extern bool ContinueDebugEvent(uint pid, uint tid, uint status);
  [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint code);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  static Info parent; static DebugEvent held;
  public static int Start(string path) {
   var start = new Startup { cb = Marshal.SizeOf<Startup>() };
   if (!CreateProcess(path, null, IntPtr.Zero, IntPtr.Zero, false, 2, IntPtr.Zero, System.IO.Path.GetDirectoryName(path), ref start, out parent)) throw new System.ComponentModel.Win32Exception();
   var deadline = DateTime.UtcNow.AddSeconds(15);
   while (DateTime.UtcNow < deadline) {
    DebugEvent e;
    if (!WaitForDebugEvent(out e, 1000)) continue;
    if (e.code == 1 && e.exception == 0x80000003) { held=e; return parent.pid; }
    ContinueDebugEvent(e.pid,e.tid,0x00010002);
   }
   Stop(); throw new Exception("Old executable did not reach initial debugger breakpoint.");
  }
  public static void Stop() {
   if (parent.process == IntPtr.Zero) return;
   TerminateProcess(parent.process,0);
   if (held.pid != 0) ContinueDebugEvent(held.pid,held.tid,0x00010002);
   DebugEvent e;
   while (WaitForDebugEvent(out e,1000)) {
    ContinueDebugEvent(e.pid,e.tid,0x00010002);
    if (e.code==5) break;
   }
   CloseHandle(parent.thread); CloseHandle(parent.process); parent=default; held=default;
  }
 }
}
"@
    }
    $old = [Diagnostics.Process]::GetProcessById([MediaStudioSmoke.DebugParent]::Start($target))
    if ($old.MainModule.FileName -ne $target) { throw 'Old parent executable identity is not readable.' }
    $actualOld = [Diagnostics.FileVersionInfo]::GetVersionInfo($target).FileVersion
    if ([Version]$actualOld -ne $oldVersion) { throw "Unexpected baseline version: $actualOld" }
    # Real updates run a helper copied from the currently installed version.
    if (!$UpdaterAppPath) { $UpdaterAppPath = $OldAppPath }
    $helperPath = Join-Path $stage 'MediaStudio.UpdateHelper.exe'
    Copy-Item -LiteralPath $UpdaterAppPath -Destination $helperPath
    $helperStart = [Diagnostics.ProcessStartInfo]::new($helperPath)
    $helperStart.UseShellExecute = $false
    $helperStart.Environment['MEDIASTUDIO_APPDATA_DIR'] = $dataDir
    foreach ($arg in @('--apply-update', '--target', $target, '--source', $source, '--release-url', "https://github.com/CarToon890/MediaStudio/releases/tag/v$($newVersion.ToString(3))", '--version', $newVersion.ToString(3), '--parent-pid', "$($old.Id)")) {
        $helperStart.ArgumentList.Add($arg)
    }
    $helper = [Diagnostics.Process]::Start($helperStart)
    # Allow cold single-file extraction/AV scanning before parent validation.
    Start-Sleep -Seconds 15
    if ($helper.HasExited) { if (Test-Path -LiteralPath (Join-Path $dataDir 'update-result.json')) { Write-Output (Get-Content -LiteralPath (Join-Path $dataDir 'update-result.json') -Raw) }; throw 'Updater exited before parent shutdown.' }
    [MediaStudioSmoke.DebugParent]::Stop()
    $old.WaitForExit()
    if (!$helper.WaitForExit(30000)) { throw 'Updater timed out.' }
    if ($helper.ExitCode -ne 0) {
        $failure = Join-Path $dataDir 'update-result.json'
        if (Test-Path -LiteralPath $failure) { Write-Output (Get-Content -LiteralPath $failure -Raw) }
        throw "Updater failed: $($helper.ExitCode)"
    }
    if (!(Test-Path -LiteralPath (Join-Path $stage 'startup-ready'))) { throw 'New app did not report healthy startup.' }
    if ([Version][Diagnostics.FileVersionInfo]::GetVersionInfo($target).FileVersion -ne $newVersion) { throw 'Target version not upgraded.' }
    if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $NewAppPath).Hash) { throw 'Installed bytes differ from new portable.' }
    if (Test-Path -LiteralPath ($target + '.update-backup')) { throw 'Successful update left backup.' }
    Write-Output "PASS: portable $($oldVersion.ToString(3)) -> $($newVersion.ToString(3)) replacement, restart and startup health marker (helper $([Diagnostics.FileVersionInfo]::GetVersionInfo($UpdaterAppPath).FileVersion))."
}
finally {
    if ('MediaStudioSmoke.DebugParent' -as [type]) { [MediaStudioSmoke.DebugParent]::Stop() }
    # Kill only test processes resolved inside this unique sandbox.
    Get-Process MediaStudio -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force
    foreach ($p in @($old, $helper)) {
        if ($p) { if (!$p.HasExited) { $p.Kill($true); $p.WaitForExit() }; $p.Dispose() }
    }
    foreach ($directory in @($root, $stage)) {
        $absolute = [IO.Path]::GetFullPath($directory)
        $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (!$absolute.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside Temp.' }
        for ($attempt = 0; $attempt -lt 10; $attempt++) {
            try { if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force -ErrorAction Stop }; break }
            catch { if ($attempt -eq 9) { throw }; Start-Sleep -Milliseconds 300 }
        }
    }
}
