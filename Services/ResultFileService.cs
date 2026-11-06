using System.Diagnostics;
using System.IO;

namespace MediaStudio.Services;

public static class ResultFileService
{
    public static string Open(string path, bool folder = false)
    {
        if (!File.Exists(path)) return "ไม่พบไฟล์ผลลัพธ์ ไฟล์อาจถูกย้ายหรือลบแล้ว";
        try
        {
            if (folder) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return string.Empty;
        }
        catch { return "เปิดผลลัพธ์ไม่ได้ ตรวจสอบสิทธิ์หรือโปรแกรมที่ใช้เปิดไฟล์นี้"; }
    }
}
