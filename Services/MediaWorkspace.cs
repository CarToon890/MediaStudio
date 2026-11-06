using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaStudio.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaStudio.Services;

public sealed class MediaWorkspace : ObservableObject
{
    private readonly string _historyPath;
    private bool _historyUnreadable;
    private string _persistenceWarning = string.Empty;
    public string PersistenceWarning
    {
        get => _persistenceWarning;
        private set { if (SetProperty(ref _persistenceWarning, value)) OnPropertyChanged(nameof(HasPersistenceWarning)); }
    }
    public bool HasPersistenceWarning => !string.IsNullOrEmpty(PersistenceWarning);
    public ObservableCollection<MediaAsset> Assets { get; } = new();
    public event Action<MediaAsset, ToolDestination>? TransferRequested;

    public MediaWorkspace() : this(null) { }

    public MediaWorkspace(string? historyPath)
    {
        if (string.IsNullOrWhiteSpace(historyPath))
        {
            var appData = Environment.GetEnvironmentVariable("MEDIASTUDIO_APPDATA_DIR");
            if (string.IsNullOrWhiteSpace(appData)) appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaStudio");
            _historyPath = Path.Combine(appData, "workspace.json");
        }
        else
        {
            _historyPath = historyPath;
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
            // File.Exists hides permission errors; open directly so they become visible warnings.
            var items = JsonSerializer.Deserialize<List<MediaAsset>>(File.ReadAllText(_historyPath))
                ?? throw new JsonException("ประวัติไม่มีรายการที่อ่านได้");
            if (items.Any(item => item is null || string.IsNullOrWhiteSpace(item.FilePath)))
                throw new JsonException("พบรายการประวัติที่ไม่สมบูรณ์");
            foreach (var item in items.Where(x => File.Exists(x.FilePath)).Take(50)) Assets.Add(item);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex)
        {
            _historyUnreadable = true;
            PersistenceWarning = $"อ่านประวัติคลังงานไม่ได้: {DescribePersistenceError(ex)} เก็บประวัติเดิมไว้แล้ว งานใหม่ยังทำได้แต่จะยังไม่บันทึกประวัติ";
        }
    }

    private static string DescribePersistenceError(Exception error) => error switch
    {
        JsonException => "ไฟล์ประวัติเสียหายหรือมีรูปแบบไม่ถูกต้อง",
        UnauthorizedAccessException => "ไม่มีสิทธิ์เข้าถึงไฟล์หรือโฟลเดอร์ประวัติ",
        IOException => "เข้าถึงไฟล์หรือโฟลเดอร์ประวัติไม่ได้ กรุณาปิดโปรแกรมที่ใช้ไฟล์นี้และตรวจสอบพื้นที่ว่าง",
        _ => "เกิดข้อผิดพลาดขณะเข้าถึงประวัติ"
    };

    private void Save()
    {
        if (_historyUnreadable) return; // Never overwrite history we could not read.
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_historyPath))!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $"workspace-{Guid.NewGuid():N}.partial");
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Assets, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _historyPath, overwrite: true);
            PersistenceWarning = string.Empty;
        }
        catch (Exception ex)
        {
            PersistenceWarning = $"บันทึกประวัติคลังงานไม่ได้: {DescribePersistenceError(ex)} ไฟล์ผลลัพธ์ยังเปิดใช้งานได้ ตรวจสอบสิทธิ์หรือพื้นที่จัดเก็บ";
        }
        finally
        {
            try { if (temporaryPath != null && File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
