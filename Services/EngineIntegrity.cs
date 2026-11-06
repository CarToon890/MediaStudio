using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace MediaStudio.Services;

public static class EngineIntegrity
{
    public static async Task<(string Url, string Hash)> ResolveAsync(HttpClient client, string repository, string assetName, CancellationToken ct)
    {
        var endpoint = repository == "yt-dlp/FFmpeg-Builds" ? "releases/tags/latest" : "releases/latest";
        using var response = await client.GetAsync($"https://api.github.com/repos/{repository}/{endpoint}", ct);
        response.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        foreach (var asset in release.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != assetName) continue;
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com"
                || !uri.AbsolutePath.StartsWith("/" + repository + "/releases/download/", StringComparison.Ordinal))
                throw new InvalidDataException("แหล่งดาวน์โหลด engine ไม่ถูกต้อง");
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71
                || !digest[7..].All(Uri.IsHexDigit)) throw new InvalidDataException("ต้นทางยังไม่มี SHA-256 ที่ตรวจสอบได้ จึงยังไม่ติดตั้ง engine");
            return (url, digest[7..]);
        }
        throw new InvalidDataException("ไม่พบ engine สำหรับ Windows x64 ใน release");
    }

    public static async Task VerifyAsync(string path, string expectedHash, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
        if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 ของ engine ไม่ตรงกับข้อมูล release ยกเลิกการติดตั้ง");
    }
}
