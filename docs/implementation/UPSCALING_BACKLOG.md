# Deferred: video upscaling

User decision: defer upscaling; do not enable it in the current Download workflow.
The current implementation only downsizes to the selected standard box or keeps a
smaller source unchanged, displaying requested/source/output dimensions separately.

Future direction: standard FFmpeg scaling, not AI/model downloads. Prefer native
website resolution first. Any upscale must state the actual downloaded source
size and resulting size and explain that additional original detail is not
recovered. Retain aspect ratio, orientation, safe cancellation, CPU fallback and
atomic output publication. No UI option or implementation is enabled by this note.
