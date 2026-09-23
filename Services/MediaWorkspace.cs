using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaStudio.Models;

namespace MediaStudio.Services;

public sealed class MediaWorkspace
{
    private readonly string _historyPath;
    public ObservableCollection<MediaAsset> Assets { get; } = new();
    public event Action<MediaAsset, ToolDestination>? TransferRequested;

    public MediaWorkspace() : this(null) { }

    public MediaWorkspace(string? historyPath)
    {
        if (string.IsNullOrWhiteSpace(historyPath))
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaStudio");
            Directory.CreateDirectory(appData);
            _historyPath = Path.Combine(appData, "workspace.json");
        }
        else
        {
            _historyPath = historyPath;
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
        }
        Load();
    }

    public MediaAsset? Register(string path, string sourceTool, double durationSeconds = 0)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        path = Path.GetFullPath(path);
        var existing = Assets.FirstOrDefault(x => string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) Assets.Remove(existing);
        var asset = new MediaAsset
        {
            FilePath = path,
            DisplayName = Path.GetFileName(path),
            Kind = DetectKind(path),
            SourceTool = sourceTool,
            CreatedAt = DateTime.Now,
            DurationSeconds = durationSeconds
        };
        Assets.Insert(0, asset);
        while (Assets.Count > 50) Assets.RemoveAt(Assets.Count - 1);
        Save();
        return asset;
    }

    public void Remove(MediaAsset? asset)
    {
        if (asset != null && Assets.Remove(asset)) Save();
    }

    public void RequestTransfer(MediaAsset? asset, ToolDestination destination)
    {
        if (asset is { } validAsset && File.Exists(validAsset.FilePath)) TransferRequested?.Invoke(validAsset, destination);
    }

    public static MediaKind DetectKind(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".wmv") return MediaKind.Video;
        if (ext is ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" or ".ogg" or ".opus") return MediaKind.Audio;
        if (ext is ".gif" or ".png" or ".jpg" or ".jpeg" or ".webp") return MediaKind.Image;
        return MediaKind.Other;
    }

    public static void OpenFile(MediaAsset? asset)
    {
        if (asset is { } validAsset && File.Exists(validAsset.FilePath))
            Process.Start(new ProcessStartInfo(validAsset.FilePath) { UseShellExecute = true });
    }

    public static void OpenFolder(MediaAsset? asset)
    {
        if (asset is { } validAsset && File.Exists(validAsset.FilePath))
            Process.Start("explorer.exe", $"/select,\"{validAsset.FilePath}\"");
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_historyPath)) return;
            var items = JsonSerializer.Deserialize<List<MediaAsset>>(File.ReadAllText(_historyPath)) ?? new();
            foreach (var item in items.Where(x => File.Exists(x.FilePath)).Take(50)) Assets.Add(item);
            Save();
        }
        catch { }
    }

    private void Save()
    {
        try { File.WriteAllText(_historyPath, JsonSerializer.Serialize(Assets, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }
}
