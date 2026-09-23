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

    public SettingsService()
    {
        var isolatedDirectory = Environment.GetEnvironmentVariable("MEDIASTUDIO_APPDATA_DIR");
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
