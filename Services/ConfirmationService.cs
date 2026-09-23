using System.Windows;

namespace MediaStudio.Services;

public interface IConfirmationService
{
    bool ConfirmEngineDownload(bool isRefresh);
}

public sealed class ConfirmationService : IConfirmationService
{
    public bool ConfirmEngineDownload(bool isRefresh)
    {
        var action = isRefresh ? "ดาวน์โหลดและแทนที่เอนจินรุ่นปัจจุบัน" : "ดาวน์โหลดเอนจินที่จำเป็น";
        var detail = isRefresh
            ? "MediaStudio จะดาวน์โหลด yt-dlp และ FFmpeg รุ่นใหม่ แล้วตรวจสอบก่อนแทนที่ไฟล์เดิม หากไม่สำเร็จจะยังใช้ไฟล์เดิมได้"
            : "MediaStudio ต้องใช้ yt-dlp และ FFmpeg สำหรับดาวน์โหลด แปลง และตัดไฟล์";
        var message = $"ต้องการ{action}หรือไม่?\n\n{detail}\n\nต้องเชื่อมต่ออินเทอร์เน็ต และควรมีพื้นที่ว่างประมาณ 150–250 MB";
        return MessageBox.Show(message, "เตรียมเอนจิน MediaStudio", MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
