using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaStudio.Models;

namespace MediaStudio.Services;

public interface IAppUpdateService
{
    Task<AppUpdateCheckResult> CheckLatestAsync(Version currentVersion, CancellationToken cancellationToken = default);
    Task<StagedAppUpdate> StageUpdateAsync(AppReleaseInfo release, IProgress<AppUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);
    UpdateLaunchResult LaunchUpdater(StagedAppUpdate update);
    bool CanWriteToApplicationDirectory(out string errorMessage);
    void OpenReleasePage(string releaseUrl);
}

public sealed class AppUpdateService : IAppUpdateService
{
    public const string LatestReleaseApi = "https://api.github.com/repos/CarToon890/MediaStudio/releases/latest";
    public const string LatestReleasePage = "https://github.com/CarToon890/MediaStudio/releases/latest";

    private static readonly Regex StableTagPattern = new("^v(?<major>\\d+)\\.(?<minor>\\d+)\\.(?<patch>\\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly HttpClient _httpClient;

    public AppUpdateService() : this(CreateHttpClient()) { }

    internal AppUpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AppUpdateCheckResult> CheckLatestAsync(Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseApi, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean() ||
                root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
                return AppUpdateCheckResult.Failed("รุ่นล่าสุดไม่ใช่ stable release");

            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!TryParseStableVersion(tag, out var latestVersion))
                return AppUpdateCheckResult.Failed("หมายเลขเวอร์ชันล่าสุดมีรูปแบบไม่ถูกต้อง");

            var releaseUrl = root.TryGetProperty("html_url", out var htmlUrl)
                ? htmlUrl.GetString() ?? LatestReleasePage
                : LatestReleasePage;
            if (!IsTrustedGitHubUrl(releaseUrl)) releaseUrl = LatestReleasePage;

            var packageName = $"MediaStudio-{tag}-win-x64.zip";
            var checksumName = packageName + ".sha256";
            var packageUrl = string.Empty;
            var checksumUrl = string.Empty;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var assetName) ? assetName.GetString() : null;
                    var url = asset.TryGetProperty("browser_download_url", out var downloadUrl) ? downloadUrl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(url) || !IsTrustedGitHubUrl(url)) continue;
                    if (string.Equals(name, packageName, StringComparison.Ordinal)) packageUrl = url;
                    if (string.Equals(name, checksumName, StringComparison.Ordinal)) checksumUrl = url;
                }
            }

            var release = new AppReleaseInfo
            {
                TagName = tag,
                Version = latestVersion,
                ReleaseUrl = releaseUrl,
                PackageUrl = packageUrl,
                ChecksumUrl = checksumUrl
            };
            return AppUpdateCheckResult.Completed(release, latestVersion > NormalizeVersion(currentVersion));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AppUpdateCheckResult.Failed("หมดเวลารอการตอบกลับ กรุณาตรวจสอบอินเทอร์เน็ตแล้วลองใหม่");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return AppUpdateCheckResult.Failed("ไม่สามารถตรวจสอบเวอร์ชันได้ กรุณาตรวจสอบอินเทอร์เน็ตแล้วลองใหม่");
        }
    }

    public async Task<StagedAppUpdate> StageUpdateAsync(AppReleaseInfo release,
        IProgress<AppUpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!release.CanAutoUpdate) throw new InvalidOperationException("Release นี้ไม่มีแพ็กเกจอัปเดตอัตโนมัติครบถ้วน");
        var updateRoot = UpdateBootstrapper.UpdateRootDirectory;
        Directory.CreateDirectory(updateRoot);
        var stagingDirectory = Path.Combine(updateRoot, release.TagName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        var packageName = $"MediaStudio-{release.TagName}-win-x64.zip";
        var packagePath = Path.Combine(stagingDirectory, packageName);
        var checksumPath = packagePath + ".sha256";

        try
        {
            await DownloadAsync(release.PackageUrl, packagePath, "กำลังดาวน์โหลด MediaStudio รุ่นใหม่", progress,
                0, 90, cancellationToken);
            await DownloadAsync(release.ChecksumUrl, checksumPath, "กำลังดาวน์โหลดข้อมูลตรวจสอบ", progress,
                90, 94, cancellationToken);
            progress?.Report(new AppUpdateProgress { Message = "กำลังตรวจสอบไฟล์อัปเดต", Percentage = 95 });
            await ValidateChecksumAsync(packagePath, checksumPath, packageName, cancellationToken);

            var extractedExecutable = Path.Combine(stagingDirectory, "new", "MediaStudio.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(extractedExecutable)!);
            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
                if (files.Length != 1 || !string.Equals(files[0].FullName.Replace('\\', '/'), "MediaStudio.exe",
                        StringComparison.Ordinal))
                    throw new InvalidDataException("แพ็กเกจอัปเดตมีโครงสร้างไม่ถูกต้อง");
                files[0].ExtractToFile(extractedExecutable);
            }

            var fileVersionText = FileVersionInfo.GetVersionInfo(extractedExecutable).FileVersion;
            if (!Version.TryParse(fileVersionText, out var fileVersion) ||
                NormalizeVersion(fileVersion) != NormalizeVersion(release.Version))
                throw new InvalidDataException("เวอร์ชันของโปรแกรมที่ดาวน์โหลดไม่ตรงกับ Release");

            progress?.Report(new AppUpdateProgress { Message = "ดาวน์โหลดและตรวจสอบเรียบร้อยแล้ว", Percentage = 100 });
            return new StagedAppUpdate
            {
                Release = release,
                StagingDirectory = stagingDirectory,
                ExecutablePath = extractedExecutable
            };
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
    }

    public UpdateLaunchResult LaunchUpdater(StagedAppUpdate update)
    {
        try
        {
            if (!CanWriteToApplicationDirectory(out var error))
            {
                TryDeleteDirectory(update.StagingDirectory);
                return UpdateLaunchResult.Failed(error);
            }
            var currentExecutable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExecutable) || !File.Exists(currentExecutable))
            {
                TryDeleteDirectory(update.StagingDirectory);
                return UpdateLaunchResult.Failed("ไม่พบตำแหน่ง MediaStudio.exe ที่กำลังใช้งาน");
            }

            var helperPath = Path.Combine(update.StagingDirectory, "MediaStudio.UpdateHelper.exe");
            File.Copy(currentExecutable, helperPath, true);
            var startInfo = new ProcessStartInfo(helperPath)
            {
                UseShellExecute = false,
                WorkingDirectory = update.StagingDirectory
            };
            foreach (var argument in new[]
            {
                "--apply-update", "--parent-pid", Environment.ProcessId.ToString(), "--target", currentExecutable,
                "--source", update.ExecutablePath, "--version", update.Release.Version.ToString(3),
                "--release-url", update.Release.ReleaseUrl
            }) startInfo.ArgumentList.Add(argument);

            if (Process.Start(startInfo) is null)
            {
                TryDeleteDirectory(update.StagingDirectory);
                return UpdateLaunchResult.Failed("ไม่สามารถเริ่มตัวช่วยอัปเดตได้");
            }
            return UpdateLaunchResult.Started();
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(update.StagingDirectory);
            return UpdateLaunchResult.Failed("ไม่สามารถเริ่มการอัปเดตได้: " + ex.Message);
        }
    }

    public bool CanWriteToApplicationDirectory(out string errorMessage)
    {
        errorMessage = string.Empty;
        try
        {
            var executable = Environment.ProcessPath;
            var directory = string.IsNullOrWhiteSpace(executable) ? null : Path.GetDirectoryName(executable);
            if (string.IsNullOrWhiteSpace(directory)) throw new IOException();
            var probe = Path.Combine(directory, ".mediastudio-update-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch
        {
            errorMessage = "โฟลเดอร์โปรแกรมไม่อนุญาตให้เขียนไฟล์ กรุณาดาวน์โหลดรุ่นใหม่จากหน้า Release";
            return false;
        }
    }

    public void OpenReleasePage(string releaseUrl)
    {
        var url = IsTrustedGitHubUrl(releaseUrl) ? releaseUrl : LatestReleasePage;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    internal static bool TryParseStableVersion(string tag, out Version version)
    {
        var match = StableTagPattern.Match(tag);
        if (!match.Success)
        {
            version = new Version();
            return false;
        }
        version = new Version(int.Parse(match.Groups["major"].Value), int.Parse(match.Groups["minor"].Value),
            int.Parse(match.Groups["patch"].Value));
        return true;
    }

    internal static Version NormalizeVersion(Version version) =>
        new(Math.Max(0, version.Major), Math.Max(0, version.Minor), Math.Max(0, version.Build));

    private async Task DownloadAsync(string url, string destination, string message,
        IProgress<AppUpdateProgress>? progress, double startPercentage, double endPercentage,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentLength = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalRead += read;
            var ratio = contentLength is > 0 ? Math.Clamp((double)totalRead / contentLength.Value, 0, 1) : 0;
            progress?.Report(new AppUpdateProgress
            {
                Message = message,
                Percentage = startPercentage + (endPercentage - startPercentage) * ratio
            });
        }
        if (contentLength is >= 0 && totalRead != contentLength)
            throw new IOException("ดาวน์โหลดไฟล์อัปเดตไม่ครบ");
    }

    private static async Task ValidateChecksumAsync(string packagePath, string checksumPath, string packageName,
        CancellationToken cancellationToken)
    {
        var checksumText = (await File.ReadAllTextAsync(checksumPath, cancellationToken)).Trim();
        var match = Regex.Match(checksumText, "^(?<hash>[0-9a-fA-F]{64})\\s+\\*?(?<name>[^\\r\\n]+)$");
        if (!match.Success || !string.Equals(match.Groups["name"].Value.Trim(), packageName, StringComparison.Ordinal))
            throw new InvalidDataException("ไฟล์ checksum มีรูปแบบไม่ถูกต้อง");
        await using var stream = File.OpenRead(packagePath);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(actualHash, match.Groups["hash"].Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("checksum ของแพ็กเกจอัปเดตไม่ตรงกัน");
    }

    internal static bool IsTrustedGitHubUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MediaStudio/1.1 (Windows; win-x64)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }
}
