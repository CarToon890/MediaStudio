# MediaStudio v1.1.3

## v1.1.3 — ปรับปรุงการดาวน์โหลด ความเสถียร และการใช้ทรัพยากร

## v1.1.2 — ใช้งานง่ายและสม่ำเสมอ

- หน้าดาวน์โหลดจัดเป็นวางลิงก์ ตรวจชื่อ/คุณภาพ/ปลายทาง และเริ่มดาวน์โหลดในหน้าเดียว ข้อมูลเก่าจะใช้เริ่มงานไม่ได้หลังเปลี่ยน URL
- หน้าแปลงแสดงค่าหลักก่อน และแสดงขนาดเป้าหมายเมื่อเปิดบีบอัด MP4/MKV
- ทุกเครื่องมือแสดงและเปลี่ยนโฟลเดอร์กลางได้ใกล้ปุ่มเริ่ม งานที่เริ่มแล้วใช้ปลายทางเดิม และเก็บค่าก่อนหน้าเมื่อบันทึกการตั้งค่าไม่สำเร็จ
- ใช้ “เปิดไฟล์ / เปิดโฟลเดอร์ / ส่งต่อ” ร่วมกัน พร้อมคำแนะนำเมื่อไฟล์ผลลัพธ์หาย
- หน้าต่างต่ำสุด 960×540 DIP เมนูย่ออัตโนมัติเมื่อกว้างต่ำกว่า 1100 DIP และส่วนตั้งค่าเลื่อน/เรียงลงได้
- Home แสดงเครื่องมือและงานล่าสุดก่อนเมื่อระบบพร้อม ทุกเครื่องมือมีวิธีใช้แบบย่อและบริบทของไฟล์ที่ส่งต่อ
- รวมทรัพยากรการออกแบบไว้ใน Themes/StudioResources.xaml คงธีมมืด Fluent โทนฟ้า และไอคอนเดิม

ไม่เปลี่ยนรูปแบบ settings.json หรือ workspace.json และไม่ต้องย้ายข้อมูล ระบบอัปเดตยังเริ่มเมื่อผู้ใช้สั่งเท่านั้น

## ดาวน์โหลดไฟล์ไหนดี

### `MediaStudio-<version>-win-x64.zip`

ไฟล์สำหรับผู้ใช้งานทั่วไปบน Windows 64-bit ภายในมี `MediaStudio.exe` ซึ่งรวม .NET Runtime ไว้แล้ว จึงไม่ต้องติดตั้ง Git, .NET SDK หรือ .NET Runtime

วิธีใช้งาน:

1. ดาวน์โหลดและแตกไฟล์ ZIP ลงในโฟลเดอร์ที่เขียนได้ เช่น Downloads หรือ Documents
2. เปิด `MediaStudio.exe`
3. ในการเปิดครั้งแรก อ่านรายละเอียดแล้วเลือก `ติดตั้งตอนนี้` เพื่อดาวน์โหลด FFmpeg และ yt-dlp

### `MediaStudio-<version>-win-x64.zip.sha256`

ไฟล์ checksum สำหรับตรวจว่า Portable ZIP ดาวน์โหลดมาครบและไม่ถูกเปลี่ยนแปลง ไฟล์นี้ไม่ใช่โปรแกรมและไม่จำเป็นสำหรับการใช้งานทั่วไป

ตรวจสอบด้วย PowerShell:

```powershell
Get-FileHash .\MediaStudio-<version>-win-x64.zip -Algorithm SHA256
```

ค่าที่ได้ต้องตรงกับค่าในไฟล์ `.sha256`

### `Source code (zip)`

ซอร์สโค้ดของรุ่นนี้ในรูปแบบ ZIP ซึ่ง GitHub สร้างให้อัตโนมัติ เหมาะสำหรับนักพัฒนาที่ต้องการอ่าน แก้ไข หรือ build โปรแกรมเอง ไฟล์นี้ไม่มี .NET Runtime และไม่ใช่ Portable app

### `Source code (tar.gz)`

ซอร์สโค้ดเดียวกันในรูปแบบ TAR.GZ เหมาะกับเครื่องมือบน Linux, macOS และงานพัฒนา เนื้อหาโปรเจกต์เหมือนกับ Source code (zip)

## ข้อกำหนดและหมายเหตุ

- รองรับ Windows x64
- ต้องใช้อินเทอร์เน็ตเพื่อเตรียม FFmpeg และ yt-dlp ในครั้งแรก รวมถึงใช้ฟังก์ชันดาวน์โหลดสื่อ
- ควรแตก ZIP ก่อนเปิดโปรแกรม และเก็บโปรแกรมไว้ในโฟลเดอร์ที่ผู้ใช้มีสิทธิ์เขียน
- Windows SmartScreen อาจแสดงคำเตือน เนื่องจากโปรแกรมยังไม่มี code-signing certificate

สำหรับผู้ใช้งานทั่วไป ให้ดาวน์โหลดเฉพาะ `MediaStudio-<version>-win-x64.zip`

### ความเข้ากันได้และหน้าต่าง

- Window minimum constraints adapt to smaller scaled work areas and refresh after
  DPI/display changes. Physical monitor movement remains a manual release check.
- Audio editor controls wrap, and queue containers follow their viewport width.
- Shared typography and spacing resources retain the existing dark appearance.
- Added non-publishing verification CI and expanded simulated layout checks.

### ลดการใช้ทรัพยากรของตัวอย่างเสียงและวิดีโอ

- Pause audio/video previews when leaving the editor or minimizing; press Play
  explicitly to resume. Background conversion, download and trim jobs continue.
- Defer hidden waveform drawing, coalesce peak batches and reuse playhead visuals.
- Acquire audio output only on Play, preserving seek controls before acquisition.
- Audio device initialization failure leaves file trimming available.
- Add hardware-independent preview lifecycle checks and a lightweight test mode.

### ตรวจสอบ GPU และตัวเข้ารหัส

- Validate NVENC with a tiny on-demand encode; cache readiness to avoid repeated
  failed GPU attempts on CPU-only systems.
- Skip probing for GPU-off, audio/GIF and stream-copy jobs.
- Add explicit GPU diagnostics in Settings without changing the saved toggle.
- Report the encoder used and preserve CPU fallback/cancellation behavior.

### ดาวน์โหลดและความปลอดภัย

- Show website/native/output video sizes; downsize larger sources when needed,
  preserve native 854x480/portrait/wide formats, and never upscale.
- Keep complete downloaded originals on resize failure/cancel; publish only
  verified outputs without overwriting existing files. Ignore partial fragments.
- Exclude new tool work while app/component updates run, including Home setup.
- Preserve engine originals during failed rollback and stop validation children
  on cancellation. Check SHA-256 release digests before installing engines.
- Report actual stream encoders; audio-only MP4 avoids GPU probing. Dispose owned
  preview files when the application service provider exits.
- Target .NET 10 LTS, pin SDK/package graph, add live dependency audit CI and
  third-party notice inventory. Upscaling remains a deferred backlog item.
