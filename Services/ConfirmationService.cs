using System.Windows;

namespace MediaStudio.Services;

public interface IConfirmationService
{
    bool ConfirmEngineDownload(bool isRefresh);
    bool ConfirmApplicationUpdate(string currentVersion, string latestVersion);
}

public sealed class ConfirmationService : IConfirmationService
{
    public bool ConfirmEngineDownload(bool isRefresh)
    {
        var action = isRefresh ? "ดาวน์โหลดส่วนประกอบใหม่ทั้งหมด" : "ดาวน์โหลดส่วนประกอบที่จำเป็น";
        var detail = isRefresh
            ? "MediaStudio จะดาวน์โหลดส่วนดาวน์โหลดมีเดียและส่วนประมวลผลไฟล์ใหม่ แล้วตรวจสอบก่อนแทนที่ไฟล์เดิม หากไม่สำเร็จจะยังใช้ไฟล์เดิมได้"
            : "MediaStudio ต้องดาวน์โหลดส่วนประกอบสำหรับดาวน์โหลด แปลง และตัดไฟล์ก่อนเริ่มใช้งาน";
        var message = $"ต้องการ{action}หรือไม่?\n\n{detail}\n\nต้องเชื่อมต่ออินเทอร์เน็ต และควรมีพื้นที่ว่างประมาณ 150–250 MB";
        return MessageBox.Show(message, "เตรียมระบบ MediaStudio", MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    public bool ConfirmApplicationUpdate(string currentVersion, string latestVersion)
    {
        var message = $"ต้องการอัปเดต MediaStudio จากเวอร์ชัน {currentVersion} เป็น {latestVersion} หรือไม่?\n\n" +
                      "โปรแกรมจะดาวน์โหลดและตรวจสอบไฟล์ ปิดตัวเอง แทนที่ไฟล์เดิม แล้วเปิดขึ้นมาใหม่โดยอัตโนมัติ " +
                      "กรุณาบันทึกงานที่กำลังแก้ไขก่อนดำเนินการ";
        return MessageBox.Show(message, "อัปเดต MediaStudio", MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
