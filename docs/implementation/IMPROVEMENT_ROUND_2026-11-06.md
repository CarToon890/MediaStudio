# Improvement round — 2026-11-06

User constraint: do not use computer-control tools. This round used source edits,
CLI builds, fake HTTP, synthetic media and headless tests only. No CUA, mouse,
keyboard, desktop automation, application shutdown, GUI smoke, real website media
or user accounts were used. The user's already-running application was left alone;
new outputs use isolated artifacts directories.

## Delivered

- Engine rollback tracks individual backup/install steps and never deletes an
  untouched engine. Locked first and second backup paths preserve both originals.
- A shared work/maintenance lease covers metadata, downloading, conversion, trim,
  GPU checks and Home/startup/Settings engine installation. App update staging
  excludes new work until success/failure cleanup. Cancellation keeps the lease
  until the underlying operation has actually returned.
- Executable validation kills and waits for its owned process on cancellation or
  timeout, then propagates user cancellation.
- Download uses a per-job staging folder and atomic no-overwrite publication.
  Partial files cannot resolve as final results. Complete native files are retained
  after resize cancellation/failure, with an explicit source-open action.
- Website format sizes and actual downloaded/output dimensions are distinct.
  Native matching formats are preferred (including 854x480, portrait and wide
  aspect ratios); larger sources fit the selected box with FFmpeg; smaller sources
  remain unchanged. No upscaling. HDR resizing is explicitly unsupported and keeps
  the source. Deferred direction: UPSCALING_BACKLOG.md.
- yt-dlp ignores external config/plugins, validates HTTP(S) URLs, terminates option
  parsing before URL arguments, and redacts URLs from engine error messages.
- Real input streams determine whether GPU probing is necessary; actual FFmpeg
  stream mappings determine reported encoders. Audio-only MP4 reports AAC.
- Owned audio preview files are disposed with the application service provider.
- Newly downloaded engines are hash-checked against official GitHub release asset
  SHA-256 metadata before promotion/execution; missing/mismatched digests fail closed.
  This is integrity under the GitHub release trust boundary, not independent GPG
  signature or malware verification. Existing engines are not silently replaced.
- Target and portable profile migrated to net10.0-windows. SDK 10.0.403 is pinned
  in global.json and CI; published runtime is 10.0.14. SDK downloaded from official
  Microsoft metadata and SHA-512 verified into artifacts/dotnet10 only. No system
  SDK replacement or PATH change. The SDK first-run created an untrusted localhost
  development certificate; only that newly created certificate/key was removed,
  preserving the pre-existing certificate. Download ZIP removed after validation.
- DI package serviced to 10.0.14; package graph locked; NuGet sources restricted to
  nuget.org in repository config. Dependency audit is enabled for transitive packages
  and enforced in verification/release CI. Third-party notice inventory is embedded;
  corresponding-source/license delivery for redistributed engine binaries remains
  a release gate, not a completed legal review.

## Executed evidence

- .NET 10 build: PASS, 0 warnings, 0 errors.
- Full --core-only regression on .NET 10: PASS, exit 0. Includes six conversion
  formats, compression, queue changes/cancel, output collisions, audio analysis/
  trims, persistence, checksum validation, application rollback and interrupted
  engine downloads. No WPF window created.
- Final --safety-only on .NET 10: PASS, exit 0. Includes rollback file locks,
  maintenance exclusion (all tools and Home engine setup), partial rejection,
  URL/metadata validation, digest mismatch/missing digest, actual audio-only MP4,
  owned preview cleanup, real 1080p->720p resize, no-upscale, collision preservation,
  retained original on cancel, rounded/native-wide selection and child-process exit.
- Real yt-dlp 2026.10.26 binary SHA-256 matches its official GitHub asset digest.
  --download-local test: PASS, exit 0, real metadata/download/remux/resize workflow
  against a loopback-only ephemeral TCP server; Thai filename, 1080p->720p, audio
  retained, one completion, staging cleanup. No external media website used.
- Live GitHub metadata: both yt-dlp.exe and the selected FFmpeg ZIP publish SHA-256
  digests. Read-only check; no engine downloaded/replaced for this verification.
- Live NuGet direct/transitive vulnerability audit: PASS, exit 0, no known
  vulnerable packages reported for the current graph. The earlier DNS limitation
  was resolved through approved network access. This is not proof of zero flaws.
- Locked restore with repository NuGet.Config and audit enabled: PASS.
- IconSmoke.ps1 and git diff --check: PASS.
- Self-contained single-file portable publish: PASS.
  artifacts/publish-improved-net10/MediaStudio.exe
  Bundled Microsoft.NETCore.App and Microsoft.WindowsDesktop.App: 10.0.14.
  SHA-256: 84F23EBB218B5840189FD6D707E0D282F93E887356B7FA76DFF6E29C4355E190

## Not claimed

No GUI/startup/consent/portable-upgrade smoke was run on the user's desktop in
this round. No new physical monitor, battery, hardware-driver, NVDA or real-site
media checks were performed. Existing UI regression matrix remains available for
an isolated authorized Windows runner. CI changes were not executed on GitHub.
No release, tag, commit, remote push or user settings migration was performed.
