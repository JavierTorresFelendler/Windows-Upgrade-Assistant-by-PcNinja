# V1.1 Beta 1 — application update trial

Test prerelease, not a stable release and not RC9. Windows upgrade behavior starts
from RC8; this experiment exercises updating the assistant itself, not Windows.

- Beta builds discover only numbered Beta releases. Stable/RC builds do not
  subscribe to this preview channel.
- A new-version offer shows Regular/Critical importance, a short publisher message
  and the exact GitHub release-notes link. No executable download starts until
  **Download and run the new version** is explicitly selected.
- Download to private staging below the running executable's own directory;
  save the verified version-named EXE beside it. No Program Files relocation,
  replacement, old-file rename, numbered collision copy or forced OS restart.
- The previous EXE remains available. An existing exact new-version file must
  pass validation before reuse; a mismatched file is never overwritten.
- First run Beta 1 manually on a disposable VM. Beta 2 is intentionally not
  published yet: after the owner confirms Beta 1 opens correctly, publish Beta 2
  to demonstrate the real prompt/download/verification/close/relaunch sequence.

Current EXEs are unsigned. SHA-256 binds release bytes but does not authenticate
a publisher independently of the GitHub account. Keep normal backup/VM snapshots.
