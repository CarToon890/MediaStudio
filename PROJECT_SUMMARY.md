# 🎬 MediaStudio - Project Handover & Technical Summary

สรุปรายละเอียดโปรเจกต์ **MediaStudio** สำหรับนำไปใช้ต่อในบทสนทนาใหม่ (New Conversation) หรือใช้อ้างอิงโครงสร้างและสถานะการพัฒนา

---

## 📌 1. ภาพรวมโปรเจกต์ (Project Overview)

- **ชื่อโปรเจกต์:** MediaStudio (Windows 11 Fluent Media Suite)
- **ประเภท:** Desktop Application (.NET 9.0 WPF)
- **ดีไซน์:** Windows 11 Fluent Design (Dark Mode, Mica/Acrylic Backdrop ผ่าน `WPF-UI`)
- **สถาปัตยกรรม:** MVVM Pattern (Model-View-ViewModel) ร่วมกับ Dependency Injection (`Microsoft.Extensions.DependencyInjection`)
- **เอนจินเบื้องหลัง (Native Core Engines):**
  - `yt-dlp`: ดาวน์โหลดวิดีโอ/เสียงคุณภาพสูงจากแพลตฟอร์มต่างๆ พร้อม Asynchronous Stdout Progress Parsing
  - `FFmpeg`: แปลงไฟล์, บีบอัดลดขนาดวิดีโอ, ตัดต่อแบบ Lossless และดึงเฉพาะไฟล์เสียง
  - `NVIDIA NVENC`: เปิดใช้การ์ดจอ RTX 4050 ในการเร่งความเร็วการ Encode/Compress วิดีโอ

---

## 📍 2. ตำแหน่งไฟล์และโฟลเดอร์ (File Paths & Locations)

- **โฟลเดอร์หลักของโปรเจกต์:** `C:\Users\USER\Desktop\BUU69\MediaStudio`
- **ไฟล์ Project File:** `C:\Users\USER\Desktop\BUU69\MediaStudio\MediaStudio.csproj`
- **ไฟล์ Executable (.exe) พร้อมรัน:** `C:\Users\USER\Desktop\BUU69\MediaStudio\bin\Debug\net9.0-windows\MediaStudio.exe`
- **โฟลเดอร์เก็บเอนจินไบนารี (Auto-managed):** `C:\Users\USER\Desktop\BUU69\MediaStudio\bin\Debug\net9.0-windows\bin\` (`yt-dlp.exe`, `ffmpeg.exe`)
- **โฟลเดอร์ปลายทางบันทึกไฟล์เริ่มต้น:** `C:\Users\USER\Downloads\MediaStudio\`
- **ไฟล์บันทึกการตั้งค่าผู้ใช้:** `%AppData%\MediaStudio\settings.json`

---

## 📁 3. โครงสร้างซอร์สโค้ด (Source Code Map)

```
MediaStudio/
├── MediaStudio.csproj            # การตั้งค่า .NET 9.0, WPF, NuGets (WPF-UI, CliWrap, Mvvm, DI)
├── App.xaml / App.xaml.cs        # DI Container, Service Registration, Application Theme
├── MainWindow.xaml / .cs         # Navigation Shell Window (Downloader, Converter, Trimmer, Settings)
├── Converters/
│   └── ValueConverters.cs        # InverseBooleanConverter สำหรับ XAML Two-way / Inverse Data Binding
├── Models/
│   └── MediaModels.cs            # MediaMetadata, DownloadItem, ConversionItem, TaskState
├── Services/
│   ├── DependencyService.cs      # เช็ค/ดาวน์โหลด yt-dlp & ffmpeg อัตโนมัติ พร้อม Concurrency Lock
│   ├── YtDlpService.cs           # จัดการดึงข้อมูลและดาวน์โหลด พร้อม ExitCode & Error Tracking
│   ├── FFmpegService.cs          # แปลงไฟล์ (NVENC + CPU Fallback), Lossless Trim (-avoid_negative_ts), Extract Audio
│   └── SettingsService.cs        # จัดการอ่าน/เขียน settings.json (Download Path, NVENC toggle)
├── ViewModels/
│   ├── MainViewModel.cs          # ควบคุมการเปลี่ยนแท็บและสถานะรวมของโปรแกรม
│   ├── DownloaderViewModel.cs    # จัดการคิวดาวน์โหลด, วางลิงก์คลิปบอร์ด, ไฮไลต์ไฟล์ใน Explorer
│   ├── ConverterViewModel.cs     # จัดการคิวแปลงไฟล์, Drag & Drop, และสไลเดอร์บีบอัดวิดีโอ
│   ├── TrimmerViewModel.cs       # จัดการโหลดวิดีโอ, สไลเดอร์ Start/End Time, Lossless Cut
│   └── SettingsViewModel.cs      # จัดการตั้งค่าโฟลเดอร์, สวิตช์ GPU, ปุ่มอัปเดตเอนจิน
└── Views/
    ├── DownloaderView.xaml / .cs # หน้าจอ Downloader
    ├── ConverterView.xaml / .cs  # หน้าจอ Converter & Compressor (รองรับ Drop & DragOver)
    ├── TrimmerView.xaml / .cs    # หน้าจอ Lossless Trimmer
    └── SettingsView.xaml / .cs   # หน้าจอ Settings
```

---

## ⚙️ 4. ฟังก์ชันหลักและการทำงานเชิงลึก (Key Features)

### 1. 📥 Media Downloader
- รองรับลิงก์ YouTube, TikTok, Facebook, Twitter/X, Bilibili ฯลฯ
- แสดง Thumbnail, ชื่อคลิป, และความยาวก่อนเริ่มโหลด
- เลือกความละเอียดได้ตั้งแต่ `4K (2160p)`, `2K (1440p)`, `1080p`, `720p`, `480p` หรือ `MP3 (Audio Only)`
- รายงานความเร็ว (MB/s), % ดาวน์โหลด, และ ETA แบบเรียลไทม์
- **Quick Play (▶):** กดเปิดเล่นไฟล์ทันใจ และ **Cancel (⏹):** กดยกเลิกการดาวน์โหลดรายชิ้นได้ทันที
- **Active Badge:** แสดงตัวเลขนับจำนวนคลิปที่กำลังดาวน์โหลดใน Sidebar

### 2. 🔄 Converter & Video Compressor
- ลากไฟล์วิดีโอ/เพลงจาก Windows Explorer มาวางในหน้าต่างได้ทันที (**Drag & Drop**)
- แปลงนามสกุล: `MP4`, `MP3`, `MKV`, `GIF`, `WAV`, `FLAC`
- **Video Compressor:** คำนวณ Target Bitrate ตามความยาววิดีโออัตโนมัติ (เช่น บีบอัดให้ < 25MB สำหรับส่ง Discord/LINE โดยไม่เสียคุณภาพ)
- รองรับ **NVIDIA NVENC Hardware Acceleration** พร้อมระบบ Auto-fallback กลับเป็น CPU อัตโนมัติหากไม่มีการ์ดจอ NVIDIA
- **Quick Play & Per-task Cancel:** รองรับการเปิดเล่นไฟล์ทันทีและปุ่มยกเลิกงาน

### 3. ✂️ Lossless Video Trimmer
- **In-App Video Preview Player:** แสดงภาพวิดีโอแบบสด (`MediaElement`) พร้อมปุ่ม Play/Pause
- **Interactive Marker:** กดปุ่ม `[ 📍 ตั้งเป็นจุดเริ่ม ]` หรือ `[ 🏁 ตั้งเป็นจุดจบ ]` จากจุดที่กำลังเล่นอยู่ได้ทันที หรือเลื่อนสไลเดอร์แล้ววิดีโอกระโดดไปเฟรมนั้นอัตโนมัติ
- ตัดคลิปวิดีโอทันใจใน **1 วินาที** โดยใช้คำสั่ง `-c copy` (Stream Copy) พร้อม `-avoid_negative_ts make_zero`
- มีโหมดแยกเฉพาะไฟล์เสียง (Extract Audio as MP3)
- มีปุ่ม **Quick Play Result** เปิดดูคลิปที่ตัดได้ทันที

### 4. ⚙️ Settings & Engine Auto-Updater
- เปลี่ยนโฟลเดอร์บันทึกไฟล์ผลลัพธ์ (พร้อมไฮไลต์เลือกไฟล์ใน Explorer เมื่อเปิด)
- ตรวจเช็คและดาวน์โหลดเอนจิน `yt-dlp` และ `FFmpeg` ให้อัตโนมัติ พร้อม Concurrency Lock ป้องกันการโหลดซ้อนกัน
- รองรับ Windows 11 Fluent Active State Highlight บน Sidebar

---

## 🛠️ 5. คำสั่งสำหรับ Build และ Run (Build Commands)

```powershell
# 1. เข้าไปยังโฟลเดอร์โปรเจกต์
cd C:\Users\USER\Desktop\BUU69\MediaStudio

# 2. คอมไพล์โปรเจกต์
dotnet build

# 3. รันโปรแกรม
dotnet run
```

---

## 💬 6. ข้อความพร้อมใช้สำหรับเริ่มต้นใน Conversation ใหม่ (Prompt Template)

> *"ฉันมีโปรเจกต์ C# .NET 9 WPF ชื่อ MediaStudio อยู่ที่โฟลเดอร์ `C:\Users\USER\Desktop\BUU69\MediaStudio` ซึ่งเป็น Desktop Tool สำหรับโหลดวิดีโอ (yt-dlp), แปลง/บีบอัดไฟล์ (FFmpeg + NVENC), และตัดคลิป Lossless โปรเจกต์คอมไพล์ผ่านสมบูรณ์แล้ว สามารถอ่านสรุปเพิ่มเติมได้ที่ `C:\Users\USER\Desktop\BUU69\MediaStudio\PROJECT_SUMMARY.md` ตอนนี้ฉันต้องการ..."*
