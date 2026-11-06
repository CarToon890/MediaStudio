using System;
using System.IO;
using System.Text.Json;

namespace MediaStudio.Services;

public class AppSettings
{
    public string DownloadDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "MediaStudio");
    public bool EnableHardwareAcceleration { get; set; } = true;
    public string PreferredVideoQuality { get; set; } = "1080p";
    public string PreferredAudioFormat { get; set; } = "MP3";
    public bool HasCompletedOnboarding { get; set; }
    public bool HasSeenEnginePrompt { get; set; }
    public string LastVisitedTab { get; set; } = "Home";
}

public class SettingsService
{
    private readonly string _settingsFilePath;
    public AppSettings Settings { get; private set; }

    public event Action? OutputDirectoryChanged;

    public SettingsService() : this(null) { }

    public SettingsService(string? appDataDirectory)
    {
        var isolatedDirectory = appDataDirectory ?? Environment.GetEnvironmentVariable("MEDIASTUDIO_APPDATA_DIR");
        var appDataDir = !string.IsNullOrWhiteSpace(isolatedDirectory)
            ? isolatedDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaStudio");
        Directory.CreateDirectory(appDataDir);
        _settingsFilePath = Path.Combine(appDataDir, "settings.json");

        Settings = LoadSettings();
        Directory.CreateDirectory(Settings.DownloadDirectory);
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null) return loaded;
            }
        }
        catch { }

        return new AppSettings();
    }

    public bool TryChangeOutputDirectory(string path, out string error)
    {
        error = string.Empty;
        var previous = Settings.DownloadDirectory;
        string? staging = null;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException();
            var probe = Path.Combine(fullPath, ".mediastudio-write-" + Guid.NewGuid().ToString("N"));
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            Settings.DownloadDirectory = fullPath;
            staging = _settingsFilePath + "." + Guid.NewGuid().ToString("N") + ".partial";
            File.WriteAllText(staging, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(staging, _settingsFilePath, overwrite: true);
        }
        catch
        {
            Settings.DownloadDirectory = previous;
            error = "เปลี่ยนโฟลเดอร์ไม่ได้ ตรวจสอบสิทธิ์ พื้นที่ว่าง และไฟล์ตั้งค่าที่อาจถูกใช้งานอยู่ แล้วลองใหม่";
            return false;
        }
        finally
        {
            try { if (staging != null && File.Exists(staging)) File.Delete(staging); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        OutputDirectoryChanged?.Invoke();
        return true;
    }

    public void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch { }
    }
}
