using System;
using System.Collections.Generic;
using System.IO;

namespace MediaStudio.Services;

public static class FileNameService
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool TryNormalizeBaseName(string? value, out string normalized, out string error)
    {
        normalized = (value ?? string.Empty).Trim().TrimEnd('.', ' ');
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "กรุณาระบุชื่อไฟล์";
            return false;
        }

        if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "ชื่อไฟล์มีอักขระที่ Windows ไม่รองรับ";
            return false;
        }

        if (Reserved.Contains(normalized.Split('.')[0]))
        {
            error = "ชื่อนี้เป็นชื่อสงวนของ Windows กรุณาใช้ชื่ออื่น";
            return false;
        }

        if (normalized.Length > 180)
        {
            error = "ชื่อไฟล์ยาวเกินไป กรุณาใช้ไม่เกิน 180 ตัวอักษร";
            return false;
        }

        return true;
    }

    public static string SanitizeSuggestedName(string? value)
    {
        var name = value ?? string.Empty;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(name)) name = "MediaStudio";
        if (Reserved.Contains(name.Split('.')[0])) name += "_media";
        return name.Length > 180 ? name[..180].TrimEnd() : name;
    }

    public static string MakeUniqueBaseName(string folder, string baseName, string extension)
    {
        for (var suffix = 0; ; suffix++)
        {
            var candidate = suffix == 0 ? baseName : $"{baseName} ({suffix})";
            if (!File.Exists(Path.Combine(folder, $"{candidate}.{extension}"))) return candidate;
        }
    }
}
