using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using MediaStudio.Models;

namespace MediaStudio.Services;

public static class UpdateBootstrapper
{
    public static string UpdateRootDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "MediaStudio", "updates");

    private static string ResultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaStudio", "update-result.json");

    public static bool IsUpdateMode(IReadOnlyList<string> arguments) => arguments.Contains("--apply-update");

    public static int ApplyUpdate(IReadOnlyList<string> arguments)
    {
        string target = string.Empty;
        string source = string.Empty;
        string releaseUrl = AppUpdateService.LatestReleasePage;
        string expectedVersion = string.Empty;
        string backup = string.Empty;
        try
        {
            target = RequireArgument(arguments, "--target");
            source = RequireArgument(arguments, "--source");
            releaseUrl = RequireArgument(arguments, "--release-url");
            expectedVersion = RequireArgument(arguments, "--version");
            var parentPid = int.Parse(RequireArgument(arguments, "--parent-pid"));
            ValidateUpdateRequest(parentPid, target, source, expectedVersion);
            WaitForParent(parentPid);

            backup = target + ".update-backup";
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(target, backup);
            try
            {
                File.Move(source, target);
                var healthMarker = Path.Combine(Path.GetDirectoryName(source)!, "startup-ready");
                var startInfo = new ProcessStartInfo(target)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(target)!
                };
                startInfo.ArgumentList.Add("--update-health-marker");
                startInfo.ArgumentList.Add(healthMarker);
                startInfo.ArgumentList.Add("--updated-to");
                startInfo.ArgumentList.Add(expectedVersion);
                var updatedProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("เปิด MediaStudio รุ่นใหม่ไม่สำเร็จ");
                if (!WaitForHealthMarker(updatedProcess, healthMarker, TimeSpan.FromSeconds(20)))
                {
                    try { if (!updatedProcess.HasExited) updatedProcess.Kill(true); } catch { }
                    throw new InvalidOperationException("MediaStudio รุ่นใหม่ไม่สามารถเริ่มทำงานได้");
                }
                TryDelete(backup);
                return 0;
            }
            catch
            {
                TryDelete(target);
                if (File.Exists(backup)) File.Move(backup, target);
                throw;
            }
        }
        catch (Exception ex)
        {
            WriteFailureResult(expectedVersion, ex.Message, releaseUrl);
            if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(target)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(target)!
                    });
                }
                catch { }
            }
            return 1;
        }
    }

    public static void CompleteNormalStartup(IReadOnlyList<string> arguments)
    {
        var marker = GetArgument(arguments, "--update-health-marker");
        if (!string.IsNullOrWhiteSpace(marker))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
                File.WriteAllText(marker, "ready");
            }
            catch { }
        }

        var updatedTo = GetArgument(arguments, "--updated-to");
        if (!string.IsNullOrWhiteSpace(updatedTo))
            MessageBox.Show($"อัปเดต MediaStudio เป็นเวอร์ชัน {updatedTo} เรียบร้อยแล้ว", "อัปเดตสำเร็จ",
                MessageBoxButton.OK, MessageBoxImage.Information);

        var failure = ConsumeFailureResult();
        if (failure is not null)
        {
            var choice = MessageBox.Show(
                $"ไม่สามารถติดตั้ง MediaStudio รุ่นใหม่ได้ โปรแกรมได้คืนเวอร์ชันเดิมให้แล้ว\n\n{failure.Message}\n\nต้องการเปิดหน้า Release เพื่อดาวน์โหลดด้วยตนเองหรือไม่?",
                "อัปเดตไม่สำเร็จ", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (choice == MessageBoxResult.Yes)
            {
                var url = AppUpdateService.IsTrustedGitHubUrl(failure.ReleaseUrl)
                    ? failure.ReleaseUrl
                    : AppUpdateService.LatestReleasePage;
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            }
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            CleanupUpdateDirectories();
        });
    }

    internal static void ReplaceFilesForTest(string target, string source)
    {
        var backup = target + ".update-backup";
        if (File.Exists(backup)) File.Delete(backup);
        File.Move(target, backup);
        try
        {
            File.Move(source, target);
            File.Delete(backup);
        }
        catch
        {
            TryDelete(target);
            if (File.Exists(backup)) File.Move(backup, target);
            throw;
        }
    }

    private static void ValidateUpdateRequest(int parentPid, string target, string source, string expectedVersion)
    {
        target = Path.GetFullPath(target);
        source = Path.GetFullPath(source);
        if (!Path.GetFileName(target).Equals("MediaStudio.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ไฟล์เป้าหมายไม่ใช่ MediaStudio.exe");
        var relativeSource = Path.GetRelativePath(Path.GetFullPath(UpdateRootDirectory), source);
        if (relativeSource.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeSource))
            throw new InvalidOperationException("ไฟล์อัปเดตอยู่นอกโฟลเดอร์ที่อนุญาต");
        if (!File.Exists(source) || !File.Exists(target)) throw new FileNotFoundException("ไม่พบไฟล์สำหรับอัปเดต");
        if (!Version.TryParse(expectedVersion, out var expected)) throw new InvalidDataException("หมายเลขเวอร์ชันไม่ถูกต้อง");
        var actualText = FileVersionInfo.GetVersionInfo(source).FileVersion;
        if (!Version.TryParse(actualText, out var actual) ||
            AppUpdateService.NormalizeVersion(actual) != AppUpdateService.NormalizeVersion(expected))
            throw new InvalidDataException("เวอร์ชันของไฟล์อัปเดตไม่ตรงกับข้อมูล Release");

        using var parent = Process.GetProcessById(parentPid);
        var parentPath = parent.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(parentPath) ||
            !Path.GetFullPath(parentPath).Equals(target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ไม่สามารถยืนยันโปรแกรมต้นทางของการอัปเดตได้");
    }

    private static void WaitForParent(int parentPid)
    {
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            if (!parent.WaitForExit(60000)) throw new TimeoutException("MediaStudio รุ่นเดิมปิดไม่สำเร็จ");
        }
        catch (ArgumentException)
        {
            // The parent already exited after validation.
        }
    }

    private static bool WaitForHealthMarker(Process process, string marker, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(marker)) return true;
            if (process.HasExited) return false;
            Thread.Sleep(200);
        }
        return false;
    }

    private static string RequireArgument(IReadOnlyList<string> arguments, string name) =>
        GetArgument(arguments, name) ?? throw new ArgumentException("ไม่พบพารามิเตอร์ " + name);

    private static string? GetArgument(IReadOnlyList<string> arguments, string name)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
            if (string.Equals(arguments[index], name, StringComparison.Ordinal)) return arguments[index + 1];
        return null;
    }

    private static void WriteFailureResult(string version, string message, string releaseUrl)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultFilePath)!);
            var result = new AppUpdateCompletion
            {
                Success = false,
                Version = version,
                Message = message,
                ReleaseUrl = releaseUrl
            };
            File.WriteAllText(ResultFilePath, JsonSerializer.Serialize(result));
        }
        catch { }
    }

    private static AppUpdateCompletion? ConsumeFailureResult()
    {
        try
        {
            if (!File.Exists(ResultFilePath)) return null;
            var result = JsonSerializer.Deserialize<AppUpdateCompletion>(File.ReadAllText(ResultFilePath));
            File.Delete(ResultFilePath);
            return result;
        }
        catch { return null; }
    }

    private static void CleanupUpdateDirectories()
    {
        try
        {
            if (!Directory.Exists(UpdateRootDirectory)) return;
            foreach (var directory in Directory.EnumerateDirectories(UpdateRootDirectory))
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
