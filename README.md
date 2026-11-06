# MediaStudio

แอปเดสก์ท็อปสำหรับ Windows ที่รวมการดาวน์โหลด แปลง บีบอัด และตัดไฟล์มีเดียไว้ในโปรแกรมเดียว พัฒนาด้วย C# และ .NET 10 WPF ใช้ yt-dlp สำหรับดาวน์โหลด และ FFmpeg สำหรับประมวลผลไฟล์

## ปรับปรุง UX/UI ใน v1.1.2

- ดาวน์โหลดตามขั้นตอนในหน้าเดียว และแสดงตัวเลือกบีบอัดเมื่อเลือกใช้
- เห็นและเปลี่ยนโฟลเดอร์บันทึกได้จากทุกเครื่องมือ งานที่เริ่มแล้วใช้ปลายทางเดิม
- คำสั่งผลลัพธ์เหมือนกันทั้งแอป: เปิดไฟล์ เปิดโฟลเดอร์ และส่งต่อ
- รองรับหน้าต่างต่ำสุด 960×540 DIP พร้อมเมนูแบบย่ออัตโนมัติและคำแนะนำในแต่ละหน้า
- คงการตั้งค่า ประวัติ ธีมมืด Fluent และไอคอนจากรุ่นก่อน ไม่ต้องย้ายข้อมูล

## ความสามารถ

### ดาวน์โหลดวิดีโอและเสียง

- อ่านชื่อคลิป ภาพตัวอย่าง และความยาวจากลิงก์ก่อนดาวน์โหลด
- แก้ชื่อไฟล์ก่อนดาวน์โหลด โดยโปรแกรมตรวจชื่อที่ Windows ไม่รองรับและตั้งชื่อใหม่อัตโนมัติเมื่อมีไฟล์ชื่อซ้ำ
- เลือกความละเอียด 480p, 720p, 1080p, 1440p หรือ 2160p และดาวน์โหลดเฉพาะเสียงเป็น MP3
- แสดงความคืบหน้า ความเร็ว และเวลาที่เหลือ พร้อมยกเลิกงาน เปิดไฟล์ หรือเปิดตำแหน่งไฟล์ใน Explorer
- การรองรับเว็บไซต์และคุณภาพที่ดาวน์โหลดได้ขึ้นอยู่กับ yt-dlp และสื่อจากลิงก์นั้น

### แปลงและบีบอัดไฟล์

- เลือกหลายไฟล์หรือลากไฟล์มาวาง แล้วแปลงเป็น MP4, MP3, MKV, GIF, WAV หรือ FLAC
- ประมวลผลคิวทีละไฟล์ โดยยึดรายการและตัวเลือกเมื่อเริ่มงาน เพิ่มไฟล์ระหว่างทำงานได้เพื่อรอรอบถัดไป ส่วนไฟล์ที่ลบออกก่อนถึงคิวจะไม่ถูกประมวลผล
- เปิดโหมดบีบอัดได้เมื่อเลือก MP4 หรือ MKV ตั้งขนาดเป้าหมายได้ 5–100 MB โดยโปรแกรมประมาณ bitrate จากความยาวสื่อ ขนาดจริงอาจต่างจากเป้าหมายและคุณภาพอาจลดลง
- ใช้ NVIDIA NVENC สำหรับการเข้ารหัสวิดีโอที่รองรับ และลองใหม่ด้วย CPU เมื่อการประมวลผลด้วย GPU ล้มเหลว
- การแปลง MKV ที่ไม่ได้เปิดโหมดบีบอัดใช้ stream copy ของวิดีโอและเสียง
- จองชื่อไฟล์ผลลัพธ์แยกกันเพื่อไม่เขียนทับผลลัพธ์เดิม เช่น `clip_converted.mp4` และ `clip_converted (1).mp4`
- ยกเลิกเป็นรายไฟล์หรือทั้งชุดได้ และเปิดไฟล์ผลลัพธ์จากรายการได้

### ตัดวิดีโอ

- ดูตัวอย่างวิดีโอในแอป พร้อมเล่น หยุด และเลือกเวลาเริ่มต้นกับเวลาสิ้นสุด
- ตัดวิดีโอด้วย stream copy โดยไม่เข้ารหัสใหม่ ระยะเวลาประมวลผลขึ้นอยู่กับไฟล์ และจุดตัดอาจไม่ตรงเฟรมที่เลือกเนื่องจากตำแหน่ง keyframe
- ไฟล์ผลลัพธ์ใช้ชื่อ `<ชื่อไฟล์>_trimmed.<นามสกุล>` และเพิ่มหมายเลขอัตโนมัติเมื่อชื่อซ้ำ

### ตัดเสียง

- เปิดไฟล์ MP3, WAV, FLAC, M4A, AAC, OGG, OPUS หรือเลือกวิดีโอที่มีแทร็กเสียง
- แสดงคลื่นเสียง ไม้บรรทัดเวลา ช่วงที่เลือก และ playhead ที่คลิกหรือลากได้ พร้อมซูมเส้นเวลา
- ปรับระดับเสียง 0–200% โดยแสดงเปอร์เซ็นต์และ dB ฟังตัวอย่างได้ทันที และใช้ค่าเดียวกันตอนบันทึก
- บันทึกผลลัพธ์เป็น MP3, WAV หรือ FLAC โดยไม่เขียนทับไฟล์เดิม

### Media Workspace

- เก็บไฟล์ที่นำเข้าและผลลัพธ์ล่าสุดสูงสุด 50 รายการในคลังงานกลาง
- ส่งวิดีโอไป Converter, Video Trimmer หรือ Audio Trimmer และส่งไฟล์เสียงไป Converter หรือ Audio Trimmer ได้ทันที
- การนำรายการออกจากคลังจะไม่ลบไฟล์จริง
- หน้า Home แสดงสถานะเอนจิน ทางลัดไปยังเครื่องมือ และงานล่าสุดสำหรับผู้ใช้ใหม่

### การตั้งค่าและเอนจิน

- เปลี่ยนโฟลเดอร์ผลลัพธ์และเปิดหรือปิด hardware acceleration ได้
- ตรวจหา `yt-dlp.exe` และ `ffmpeg.exe` ในโฟลเดอร์ `bin` ใต้ตำแหน่งแอปก่อน แล้วค้นหาใน `PATH`
- ถามผู้ใช้ก่อนดาวน์โหลดเอนจินครั้งแรก และถามยืนยันทุกครั้งเมื่อกดติดตั้ง ลองใหม่ หรืออัปเดตด้วยตนเอง
- ดาวน์โหลดลงไฟล์ชั่วคราวและย้ายเป็นไฟล์ปลายทางเมื่อเสร็จ เพื่อไม่ให้ไฟล์ที่โหลดไม่ครบถูกใช้เป็นเอนจิน การแตก FFmpeg ใช้ไฟล์ชั่วคราวเช่นกัน
- ปุ่มอัปเดตเอนจินดาวน์โหลดและตรวจสอบไฟล์รุ่นใหม่ก่อนแทนที่ โดยเก็บเอนจินเดิมไว้หากการอัปเดตล้มเหลว

### การอัปเดต MediaStudio

- โปรแกรมจะไม่ตรวจหรือดาวน์โหลดเวอร์ชันใหม่เอง ผู้ใช้เป็นผู้เริ่มตรวจจากหน้า `การตั้งค่า`
- ระบบตรวจเฉพาะ stable release ล่าสุดจาก GitHub และแสดงปุ่ม `ดูรายละเอียด` กับ `อัปเดตตอนนี้` เมื่อพบเวอร์ชันใหม่
- ก่อนติดตั้ง โปรแกรมจะแสดงเวอร์ชันปัจจุบันและเวอร์ชันใหม่ให้ยืนยัน จากนั้นดาวน์โหลด Portable ZIP และตรวจ SHA-256 checksum
- เมื่อผ่านการตรวจสอบ โปรแกรมจะปิด แทนที่ `MediaStudio.exe` และเปิดเวอร์ชันใหม่ทันที หากเปิดเวอร์ชันใหม่ไม่สำเร็จ ระบบจะคืน executable เดิมและเสนอหน้า Release สำหรับดาวน์โหลดด้วยตนเอง
- การอัปเดตอัตโนมัติต้องวางโปรแกรมในโฟลเดอร์ที่ผู้ใช้มีสิทธิ์เขียน และต้องไม่มีงานดาวน์โหลดหรือประมวลผลกำลังทำงาน

## สำหรับผู้ใช้ทั่วไป

ผู้ใช้ทั่วไปไม่ต้องติดตั้ง Git, .NET SDK หรือ .NET Runtime และไม่ต้องดาวน์โหลด Source Code ZIP ของ GitHub:

1. เปิดหน้า [Latest Release](https://github.com/CarToon890/MediaStudio/releases/latest)
2. ดาวน์โหลด `MediaStudio-<version>-win-x64.zip`
3. แตกไฟล์ทั้งหมดลงโฟลเดอร์ที่เขียนได้ เช่น Downloads หรือ Documents อย่าเปิดโปรแกรมจากภายใน ZIP
4. เปิด `MediaStudio.exe`
5. เมื่อเปิดครั้งแรก เลือก `ติดตั้งตอนนี้` ในกล่องยืนยัน แล้วรอให้โปรแกรมดาวน์โหลด FFmpeg และ yt-dlp

หลังติดตั้งแล้ว สามารถเข้า `การตั้งค่า` และกด `ตรวจสอบเวอร์ชันใหม่` เมื่อต้องการตรวจ stable release ล่าสุด โปรแกรมจะไม่ตรวจเวอร์ชันในพื้นหลัง

แพ็กเกจ Portable รองรับ Windows x64 และรวม .NET Runtime ไว้แล้ว ต้องใช้อินเทอร์เน็ตในการเตรียมเอนจินครั้งแรกและดาวน์โหลดสื่อ เนื่องจากโปรแกรมยังไม่มี code-signing certificate Windows SmartScreen อาจแสดงคำเตือนก่อนเปิดใช้งาน

ผู้ใช้ต้องมีสิทธิ์เขียนในโฟลเดอร์ที่แตกไฟล์ เพราะโปรแกรมเก็บเอนจินไว้ในโฟลเดอร์ `bin` ข้าง executable หากต้องการใช้ NVIDIA NVENC ต้องมี NVIDIA GPU และไดรเวอร์ที่รองรับ

## สำหรับนักพัฒนา

ต้องมี Git และ .NET 10 SDK จากนั้นรันคำสั่งใน PowerShell:

```powershell
git clone https://github.com/CarToon890/MediaStudio.git
cd MediaStudio
dotnet restore
dotnet run --project MediaStudio.csproj
```

คำสั่งข้างต้นใช้สำหรับพัฒนาจากซอร์สโค้ด ผู้ใช้งานทั่วไปควรใช้ Portable ZIP จากหน้า Releases

Build แบบ Release:

```powershell
dotnet build MediaStudio.csproj -c Release
```

เปิดไฟล์ที่ build แล้ว:

```powershell
.\bin\Release\net9.0-windows\MediaStudio.exe
```

การ build ปกติยังต้องใช้ .NET Desktop Runtime 9 บนเครื่องที่รัน หาก build ติดข้อผิดพลาดว่าไฟล์ถูกใช้งาน ให้ปิดแอปที่รันจากโฟลเดอร์ผลลัพธ์นั้นแล้วลองใหม่

สร้าง Portable executable แบบเดียวกับที่ใช้ใน GitHub Releases:

```powershell
dotnet publish MediaStudio.csproj -p:PublishProfile=win-x64
```

ผลลัพธ์อยู่ใน `bin\Release\net9.0-windows\win-x64\publish\` และรวม .NET Runtime ไว้แล้ว

## ไฟล์และการตั้งค่า

- ผลลัพธ์เริ่มต้น: `%USERPROFILE%\Downloads\MediaStudio\` เปลี่ยนได้ใน Settings และใช้ร่วมกันสำหรับดาวน์โหลด แปลงไฟล์ และตัดคลิป
- การตั้งค่าผู้ใช้: `%APPDATA%\MediaStudio\settings.json`
- ประวัติคลังงาน: `%APPDATA%\MediaStudio\workspace.json`
- ผลลัพธ์ Debug: `bin\Debug\net9.0-windows\`
- ผลลัพธ์ Release: `bin\Release\net9.0-windows\`
- ผลลัพธ์ Portable: `bin\Release\net9.0-windows\win-x64\publish\MediaStudio.exe`
- เอนจินที่แอปดาวน์โหลด: โฟลเดอร์ `bin\` ใต้ตำแหน่ง executable เช่น `bin\Release\net9.0-windows\bin\`
- `bin/` และ `obj/` ไม่ถูกเก็บใน Git ตาม `.gitignore` จึงต้อง build และเตรียมเอนจินหลัง clone

## การออกรุ่น

เมื่อ push tag ที่ตรงรูปแบบ semantic version เช่น `v1.1.0` GitHub Actions จะ build และทดสอบโปรเจกต์ จากนั้น publish สำหรับ Windows x64 สร้าง ZIP กับไฟล์ SHA-256 checksum และแนบทั้งสองไฟล์กับ GitHub Release อัตโนมัติ

```powershell
git tag v1.1.0
git push origin v1.1.0
```

ควรสร้าง tag หลังตรวจการทำงานบน Windows x64 แล้วเท่านั้น Source Code ZIP/TAR ที่ GitHub สร้างอัตโนมัติไม่มี .NET Runtime และไม่ใช่แพ็กเกจ Portable สำหรับผู้ใช้

## โครงสร้างโปรเจกต์

```text
MediaStudio/
  App.xaml / App.xaml.cs          ธีมแอปและ Dependency Injection
  MainWindow.xaml / .xaml.cs      หน้าต่างหลักและการนำทาง
  MediaStudio.csproj             การตั้งค่า build และแพ็กเกจ
  Converters/                    ตัวแปลงค่าสำหรับ XAML binding
  Models/                        ข้อมูลสื่อ รายการงาน และสถานะ
  Services/
    DependencyService.cs         เตรียมเอนจินและดาวน์โหลดไฟล์ชั่วคราว
    FFmpegService.cs             แปลง บีบอัด ตัดคลิป และอ่านความยาว
    FileNameService.cs           ตรวจชื่อไฟล์และสร้างชื่อที่ไม่ซ้ำ
    MediaWorkspace.cs            เก็บประวัติและส่งไฟล์ระหว่างเครื่องมือ
    SettingsService.cs           อ่านและบันทึก settings.json
    YtDlpService.cs              อ่านข้อมูลสื่อและดาวน์โหลด
  ViewModels/                    คิวงาน คำสั่ง และสถานะหน้าจอ
  Views/                         หน้า Home, Downloader, Converter, Trimmer, Audio Trimmer, Workspace และ Settings
  Tests/                         ชุดทดสอบ regression และวิธีรัน
  Tools/                         สคริปต์ช่วยพัฒนาและสร้างไอคอน
  docs/reviews/                  บันทึกการรีวิว UX
  artifacts/releases/            ไฟล์ ZIP ของแต่ละรุ่นและ SHA256 (เฉพาะในเครื่อง)
  artifacts/reviews/             ภาพและไฟล์ประกอบการรีวิว (เฉพาะในเครื่อง)
  RELEASE_NOTES.md               แม่แบบคำอธิบายไฟล์ใน GitHub Release
```

ใช้รูปแบบ MVVM และลงทะเบียน service กับ view model ผ่าน Dependency Injection แพ็กเกจที่กำหนดในโปรเจกต์คือ CliWrap 3.10.5, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection 10.0.11, NAudio 2.2.1 และ WPF-UI 4.3.0

บันทึกการรีวิวหน้าจออยู่ที่ [UX Review](docs/reviews/UX_REVIEW.md) ส่วนโฟลเดอร์ `bin/`, `obj/`, `Tests/bin/` และ `Tests/obj/` เป็นผลลัพธ์ที่สร้างใหม่ได้ด้วยการ build หรือรันทดสอบ โฟลเดอร์เหล่านี้อาจยังไม่มีหลังทำความสะอาดโปรเจกต์ ส่วน `artifacts/publish*` เก็บชุด publish ที่ยังใช้งาน โดยคงตำแหน่งชุดที่เอกสารทดสอบอ้างอิงไว้

## การทดสอบ

ชุดทดสอบ regression เป็น console application ที่ใช้ FFmpeg จริงกับสื่อที่สร้างขึ้นสำหรับทดสอบ และใช้ HTTP handler จำลองสำหรับการดาวน์โหลดเอนจิน ไม่เรียกเครือข่ายหรืออ่านและแก้ไขการตั้งค่าที่ผู้ใช้บันทึกไว้ระหว่างทดสอบ

รันจากโฟลเดอร์ repository โดยระบุพาธ FFmpeg ที่มีอยู่จริง:

```powershell
dotnet run --project Tests/MediaStudio.RegressionTests.csproj -c Release -- "C:\path\to\ffmpeg.exe"
```

ครอบคลุมกรณีต่อไปนี้:

- เก็บไฟล์ผลลัพธ์เดิมและแยกผลลัพธ์ของไฟล์ต้นทางชื่อซ้ำ
- แปลงครบทั้ง 6 ฟอร์แมตเมื่อมีการร้องขอโหมดบีบอัด
- เพิ่ม ลบ และล้างรายการที่เสร็จแล้วระหว่างแปลง โดยคิวเดิมที่เหลือยังทำงานต่อได้
- ยกเลิกงานและคืนสถานะคิว
- ตรวจชื่อไฟล์ภาษาไทย ชื่อสงวน อักขระต้องห้าม และชื่อซ้ำ
- สร้าง waveform ตัดเสียงเป็น MP3/WAV/FLAC และบันทึก/โหลดคลังงาน
- ดาวน์โหลดไม่ครบหรือถูกยกเลิก โดยไม่เผยแพร่ไฟล์บางส่วน ไม่แทนที่เอนจินเดิม และลองใหม่สำเร็จ

ชุดทดสอบอัตโนมัติยังไม่ครอบคลุมการใช้งานหน้าจอทั้งหมด การดาวน์โหลดจากเว็บไซต์จริง หรือการเข้ารหัสด้วย NVENC บน GPU รายละเอียดเพิ่มเติมอยู่ใน [คู่มือทดสอบ](Tests/README.md)

### Foundation verification

The development verification workflow runs on pull requests or manual dispatch;
it builds, runs regression/icon checks, publishes a local CI artifact for startup
and engine-consent smoke tests, and uploads UI captures. It does not create releases.
See [implementation progress](docs/implementation/V1_1_2_PROGRESS.md) for completed
work and [manual DPI matrix](Tests/Manual/DPI_Matrix.md) for device checks.

## Preview efficiency and device checks

Preview now pauses when its tab is hidden or the window is minimized. Restoring
the tab/window does not start playback automatically. File conversion, downloads
and trim operations continue independently. Audio output devices are opened on
Play, released on hiding/unloading, and reopened on the next Play request. A device
initialization failure shows a recovery message and keeps trimming available.

Waveform updates in one dispatcher turn are coalesced into a render, and hidden
waveforms defer drawing until visible. Playhead updates reuse the existing overlay
objects and waveform bars share a frozen brush. Foreground playback timing and
encoding settings are unchanged; no wattage or battery-life improvement is claimed
without physical measurement.

## GPU readiness and CPU compatibility

The hardware toggle is a preference. GPU readiness is checked only on demand:
Settings > ตรวจสอบ GPU, or the first video encode requesting NVIDIA NVENC.
A single synthetic 128x128 frame is encoded without writing a media file. There
is no startup/background probe, network request or automatic engine download.
Audio/GIF conversions and MKV stream copies do not probe NVIDIA hardware.

Results are cached for five minutes for the current FFmpeg path, length and
modification time. Engine changes invalidate the result. Inconclusive results
cache for one minute. Driver/hardware changes can be rechecked immediately with
the Settings button. Readiness does not guarantee every real input can use GPU;
a failed GPU encode still falls back once to CPU unless cancelled. Completed
jobs show the encoder actually used and retain the fallback explanation.

## Download resolution and safety update

Download now displays the highest resolution reported by the website and verifies
actual downloaded video dimensions. Selecting a lower resolution uses a matching
native stream where available, otherwise FFmpeg downsizes within a standard
landscape/portrait box, preserving aspect ratio. A smaller source is kept at its
actual size: this release does not upscale. Each job shows selected, source and
output sizes, and original files retained after a failed/cancelled resize can be
opened from that job. HDR resizing currently stops with an explanation and keeps
the source; it does not silently convert HDR colors.

Tool work and application/component updates now share an exclusion mechanism;
engine rollback preserves untouched originals. Partial downloads cannot be chosen
as completed outputs. yt-dlp runs with isolated config/plugin settings and accepts
only HTTP(S) URLs. New engine downloads are verified against the official GitHub
release asset SHA-256 digest before promotion or execution. Missing/mismatched
verification data blocks installation. This does not independently verify an
upstream maintainer's identity or replace GPG/code-signature verification.

The project now targets .NET 10 LTS, pins SDK 10.0.403 in global.json/CI, uses
packages.lock.json and the repository NuGet.Config, and audits transitive packages.
For this workspace the SDK is available at artifacts/dotnet10/dotnet.exe; it was
not installed over the system SDK. Restore/build with that executable, or install
the matching SDK through your usual development setup. Third-party notices are in
THIRD_PARTY_NOTICES.md and embedded in the executable. Binary-specific engine
license/source-delivery checks remain required before redistribution.
