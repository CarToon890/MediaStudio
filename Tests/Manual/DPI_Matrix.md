# DPI matrix

Smallest simulated work area in the automated matrix is 780 x 406 DIP. At smaller
physical work areas the window minimum also shrinks, but operability is unverified.
The original preferred minimum remains 960 x 540 DIP when the work area allows it.
Pages scroll vertically; the audio waveform alone scrolls horizontally by design.

| Physical display | Scale | Result / evidence |
|---|---|---|
| 1366 x 768 | 125%, 150% | NOT RUN |
| 1920 x 1080 | 100%, 150% | NOT RUN |
| 2560 x 1440 | 200% | NOT RUN |
| Unequal DPI monitors | move/unplug/restore | NOT RUN |
| Remote desktop / snap / Alt+Tab | varied | NOT RUN |

Record Windows build, monitor work area, build SHA plus diff, and screenshots.
Check every page with long Thai labels and populated queues. Confirm critical
controls are reachable, focus stays visible, and an unplugged monitor cannot
strand the window. Physical tests must include title bar and taskbar insets.
