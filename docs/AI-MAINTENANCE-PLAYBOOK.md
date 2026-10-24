# AI maintenance playbook

This is repository-owned, reusable project know-how for any AI assistant or human
maintainer. It does not require Codex, proprietary tools or access to past chats.
Read `../AGENTS.md` first and use [Development status](DEVELOPMENT-STATUS.md) for
current work, test evidence and release holds. Verify these notes against source;
when behavior changes, update the relevant lesson and its tests together.

## Product intent

Help advanced home users perform an **in-place upgrade preserving apps and
personal files**, including an explicitly experimental unsupported-hardware path.
Never turn an unavailable upgrade into a clean or data-only installation.
This remains a standalone application. A future Windows Update tool integration
should reuse Core operations, not copy the standalone UI into another product.

Keep two main source choices on one screen: automatic download, or existing
ISO/mounted/extracted/network media. Preserve Refresh, exact source selection,
restart policy, visible warnings and Start/Cancel behavior. Prefer concise,
actionable user wording; retain technical detail in diagnostics. Repository/UI
text is English; discussion with the owner can be Hebrew.

## Code ownership map

All paths below are relative to the repository root.

| Area | Read first | Responsibility |
| --- | --- | --- |
| GUI | `src/PcNinja.Win11UpgradeAssistant/MainForm.cs` | Selection state, refresh, preparation/cancel, visible explanations |
| CLI | `src/PcNinja.Win11UpgradeAssistant/CliRunner.cs`, `CliHelp.cs` | Arguments, JSON/exit contracts, offline help/completion |
| Shared orchestration | `src/PcNinja.Win11Upgrade.Core/UpgradeEngine.cs` | OS/storage checks, mounting/validation, exact Setup launch |
| Transfer | `src/PcNinja.Win11Upgrade.Core/IsoTransfer.cs` | Exact-file ownership, partial/resume, integrity and reuse |
| Installation safety | `src/PcNinja.Win11Upgrade.Core/InstallationSafety.cs` | Selected/secondary answer files and final prelaunch gate |
| Image/language policy | `src/PcNinja.Win11Upgrade.Core/InstallationImage.cs`, `AutomaticDownloadPolicy.cs` | Actual image metadata, compatible selection and automatic-language gate |
| Outcome evidence | `src/PcNinja.Win11Upgrade.Core/UpgradeHistory.cs` | Persisted attempt and post-restart observations |
| Application updates | `src/PcNinja.Win11Upgrade.Core/AppUpdates/`, `src/PcNinja.Win11UpgradeAssistant/AppUpdateInstaller.cs` | Stable release discovery, protected staging, operation gate and app-only helper/recovery |
| Tests and delivery | `tests/`, `.github/workflows/` | Safe fixtures, client GUI checks, CLI checks and separate PR/release builds |

## Validated engineering lessons

### 1. Identify the exact source, not a likely filename

- Keep the original safe server filename, exact final path, selected source and
  source-specific image index. Never guess using `Windows*`, newest ISO, or a
  universal index for Pro/Home.
- Reuse recorded/exact-name downloads only after the existing integrity/media
  checks. Do not reintroduce numbered Copy N files as an alternative to reuse.
- Only proven-corrupt, recorded app-owned downloads may be removed for retry.
  Cancellation, unavailable media, unsafe answer files and signature-chain/network
  failures do not prove a user's ISO is corrupt.
- A trusted signature on root `setup.exe` does not validate every image byte.
  DISM metadata readability is not a full integrity scan; a calculated SHA-256
  without an independently trusted expected value is only an identifier.

### 2. Treat unattended media as a data-loss hazard

- A different attached ISO/USB can contain an interfering answer file even if the
  selected source is correct. Preserve attached-media checks, not just a check of
  the chosen setup folder.
- Remove only narrowly scoped writable answer files on the selected valid source
  under the existing safety rules. Block on other/read-only/locked unsafe media;
  explain the exact blocker and ask the user to detach it and Refresh.
- Do not delete Windows-owned cached files broadly, auto-unmount arbitrary media,
  add a bypass, select a disk/partition, or fall back to `/auto clean`.
- Repeat safety and source/index validation immediately before launch. A passed
  earlier scan does not cover media added or modified during preparation.
- Automatic-flow consent must precede transfer and belong only to that attempt's
  successful exact source and restart intent. A browser fallback or later manual
  Start must not inherit it. Never remove safety checks to eliminate a UI prompt.

### 3. Separate compatibility from media readability

- Successful image enumeration does not mean an upgrade is allowed. Validate the
  actual edition (including N/Single Language), architecture, system-default UI
  language and target build. Never trust the ISO filename for these properties.
- DISM output contains a tool version and an image version. Parse the image detail
  block, not the first `Version` line. Read every actual index's metadata.
- English variants such as en-US/en-GB are not interchangeable under the current
  strict language policy. Keyboard language is not the system-default UI language.
- Automatic download availability follows the configured image language in shared
  Core logic, not only a disabled GUI control. Manual matching media has no
  English-only restriction. A higher build alone does not prove a supported path.
- Windows 11 targets retain equal-or-newer build eligibility. The owner-scoped
  manual Windows 10 POC intentionally does not compare intra-Windows-10 build
  order; preserve edition/language/x64/client gates and Windows 11 -> 10 blocking.
  This is a test policy delegated to Setup, not a guarantee that downgrades work.
  Do not reintroduce build-order checks or broaden this exception silently.
- Select the command profile from actual image metadata, never the ISO/folder
  name or installed OS alone. Windows 10 omits `/product server` and `/eula accept`;
  Windows 11 retains its existing profile. Automatic download stays Windows 11-only.
- Windows 10 2004-22H2 share system files. In the intentionally simple POC,
  raw DISM image metadata is not an independently verified feature-release build.
  Do not infer 22H2 from a filename or report automatic target-build confirmation
  from a shared base. No offline registry extraction was implemented; manual
  outcome verification is required. See [Windows 10 POC](WINDOWS10-POC.md).

### 4. Distinguish storage and transport failures

- ISO-destination space and Windows-drive upgrade workspace are separate gates.
  Moving the ISO to USB does not fix inadequate Windows-drive space. Derive the
  Windows volume from Windows' actual location; do not assume C: unconditionally.
- For TLS/network issues, keep the inner error and route details for diagnostics,
  retain normal certificate validation and use the existing secure provider
  fallback. Never disable TLS validation to make a download work.
- Public browser/manual selection and the internal automatic transport are separate.
  Do not replace the direct-provider transport with the manual catalog URL.
- Wait for a mounted ISO's drive letter with a bounded timeout; mount success is
  not proof that the volume is ready for immediate validation.
- PowerShell stderr can contain CLIXML combining progress records with genuine
  errors. Diagnose the exit code and decoded error records separately; do not
  treat all stderr as fatal or discard it as progress. Keep raw diagnostics out of
  the user-facing status message. A successful ISO hash is not a completed mount
  or DISM validation, and a drive-letter discovery timeout does not prove a
  corrupt ISO. Record the failing stage explicitly.
- Distinguish an image left mounted from an image proven locked against eject.
  Access denied when deleting a file on read-only ISO media does not establish a
  process/handle leak. Before proposing detach cleanup, establish exact image
  identity, mount ownership, running-from-media state and active consumers; an
  awaited/disposed helper is not by itself evidence of an orphaned process.

### 5. Keep UI, CLI and outcome evidence consistent

- Put operational safety in Core and call it from both interfaces. Add tests for
  both presentation modes when availability, selection or exit behavior changes.
- Prove automatic eligibility at the real download entry with a fail-if-called
  fake HTTP handler and no destination creation, not only by testing a predicate.
  Keep positive own-edition coverage and original transport behavior. Eligibility
  trusts configured media coverage; actual downloaded bytes still need validation.
- Visual next-action cues are state presentation, never operational authorization.
  Clear the previous cue and stale displayed preparation on recheck/source change;
  preserve automatic mode and red Cancel/Setup lockout. Resolve Downloads via the
  user's Windows known folder, not a hardcoded English or C: path. Restore CWD
  after browsing; do not probe network paths on every edit or add a polling timer.
- Host eligibility, configured-download eligibility and actual image compatibility
  are three separate decisions. Shared `ManualUpgradePolicy` permits exact manual
  client LTSC media without enabling consumer downloads or Server execution. A
  passed image inspection must not advertise Start on a blocked host. Use one
  detected GUI profile for both host and image guidance, preserve mismatch details
  and repeat actual host/media checks at final Core launch. Test both a valid LTSC
  case and invalid architecture/type/edition/language cases; screenshot evidence
  that Setup offers Keep apps/files is not a completed migration result.
- A successful Core download already hashes and validates its mounted media.
  Hand that exact result to GUI/CLI rather than immediately repeating it. This
  bounded handoff is not a general cache: retain final prelaunch revalidation,
  and do not skip later manual ISO checks based only on size/timestamps.
- For later session reuse, require unchanged-file evidence and exact mount
  identity, not an attachment flag or drive letter. On fixed NTFS, verify the
  exact retained CLOSE journal record: last-USN alone is vulnerable to coalesced
  same-kind writes under a long-lived external handle. Missing/reset/trimmed
  journal evidence means full validation. Keep guards operation-scoped and repeat
  signatures/attached-media safety even on hits. Do not seed this cache from a
  fresh transfer hash made before obtaining file proof. See
  [ISO session validation](ISO-VALIDATION-REUSE.md) for the limits and safe fixtures.
- Refresh must preserve the exact source, index and restart policy when valid.
  Changing source invalidates the old index. Failure/cancel messages must survive
  the final refresh and explain what to do, not disappear as a brief flash.
- An empty image-selector cue is presentation, not an InstallationImage or index.
  Distinguish not validated, awaiting a choice and no compatible image. Reuse Core
  rejection reasons after successful enumeration; do not mislabel mount/read
  failures as incompatibility or overwrite the independent error/details notice.
  Keep real image items and existing automatic/same-source selection rules.
- Cancellation is for download/preparation. After Setup actually launches, do not
  pretend the assistant can safely cancel it. Do not force a reboot after failure.
- Root Setup process exit 0 means its current stage completed, not that all files
  and apps survived a completed migration. Record uncertain outcomes honestly and
  use post-restart build/edition observations plus owner verification.
- When automatic restart is off, exit 0 needs an explicit manual-restart
  instruction (GUI information dialog plus persistent notice, CLI text/JSON).
  Do not call that a crash or a finished OS migration. Never show a restart
  instruction as a successful next step for failure/unconfirmed transition.
- Correlate Panther timestamps with the exact recorded attempt before diagnosis;
  an old morning Appraiser error is not evidence that an evening exit-0 attempt
  crashed. Keep source metadata and process outcome separate from folder names.
- Reconcile rollback only when Setup is not in progress, the original build is
  running and the session header matches launch/media/image/index. Require
  matching terminal failure and completed rollback evidence. Keep process exit
  separate from Setup result/extended codes; 0x40010004 alone proves neither
  success nor failure. Bound reads/regex work; stale/incomplete evidence stays
  unknown. Never auto-clean Recovery following a duplicate-file failure.
- For a Setup process crash, pair the recorded attempt/source/build/index with
  Panther logs and the faulting application/module in Windows crash events.
  `0xC0000005` is an access violation, not filesystem Access Denied. Do not assign
  a root cause from that generic exception code or assume the supplied ISO is
  corrupt. Preserve evidence before retrying, and distinguish an earlier mount
  failure from a later successfully launched Setup process in the same log.
- Local signature/image-metadata/hash validation is not Windows Setup's later
  application/driver compatibility scan. An Appraiser crash after those local
  checks identifies a different validation layer; do not remove that layer to
  optimize away repeated work. Correlate the final exception with its phase;
  earlier nonfatal Error entries or adjacent inventory messages are not
  automatically root causes. A changed target Setup build is a changed test
  variable, not proof that a previously failing ISO was corrupt.
- Build >= 22000 identifies Windows 11 in this client's display normalization;
  registry ProductName alone can still say Windows 10. Keep Server detection separate.

### 6. Resize redraw is separate from a fresh screenshot

- A custom border painted from current Width/Height on an ordinary Panel can
  leave old edge lines after resize if only newly exposed strips are repainted.
  Set ResizeRedraw on the actual bordered card, with local double buffering to
  reduce flicker. Double buffering alone does not request full invalidation.
- Keep this local to the custom-painted panels; do not add global composited
  window styles, timers, Refresh loops or changes to upgrade operations.
- GUI regression should check styles and full client invalidation/paint during
  live repeated resize. DrawToBitmap or explicit Refresh/Invalidate can hide the
  original defect, so a clean static screenshot is not proof of a resize fix.
- Local client fixtures do not cover every DPI/graphics/remote-session case;
  repeat the original owner scenario on the affected machine after release.

### 7. Keep silent Setup separate from orchestration and restart authorization

- `/quiet` hides Setup UX; it must not remove source/image/storage/answer-file
  checks or imply consent. CLI requires explicit --yes for real quiet upgrades;
  no browser/popup/hidden input prompt is appropriate for remote/SYSTEM callers.
- CLI quiet and /noreboot are independent, even if the standalone GUI deliberately
  locks automatic restart when silent mode is selected. Never enforce GUI policy
  in Core so that it overrides a remote caller's --no-restart.
- Exit 0 is not a restart permission: status/validation/dry runs also return 0.
  Report stable command result identifiers and native Setup integer/hex separately
  from assistant exit groups. All failure/uncertain/non-upgrade results must have
  readyForRestart=false. PreparationCompleted is pre-reboot, not final OS success.
- Test the real result presenter with synthetic Setup exit codes without invoking
  Setup, including uncertain transition, native failures and every restart/quiet
  combination. Also test the compiled portable CLI and client UI independently;
  successful fixtures are not proof of BigFix agent wiring or live quiet upgrades.

## Safe verification recipe

Run from the repository root on a Windows client with the .NET 8 SDK. Read the
test sources first; these fixtures must never invoke Windows Setup. Do not run a
real `upgrade` command on the development machine to test a theory.

```powershell
dotnet build tests/PcNinja.Win11Upgrade.SmokeTests/PcNinja.Win11Upgrade.SmokeTests.csproj -c Debug -warnaserror
dotnet run --project tests/PcNinja.Win11Upgrade.SmokeTests/PcNinja.Win11Upgrade.SmokeTests.csproj -c Debug --no-build
dotnet build tests/PcNinja.Win11Upgrade.GuiChecks/PcNinja.Win11Upgrade.GuiChecks.csproj -c Debug -warnaserror
dotnet run --project tests/PcNinja.Win11Upgrade.GuiChecks/PcNinja.Win11Upgrade.GuiChecks.csproj -c Debug --no-build
& ./tests/Verify-Cli.ps1 -ExePath ./tests/PcNinja.Win11Upgrade.GuiChecks/bin/Debug/net8.0-windows/win-x64/Windows-Upgrade-Assistant-By-PcNinja.exe -ExpectedVersion '1.1.0-beta.1'
git diff --check
```

Use the current project version for `ExpectedVersion` after a version change.
Check each command's exit code; a later successful command does not cancel an
earlier failure. Put `-warnaserror` on `dotnet build`, not `dotnet run`, where it
can be forwarded as an application argument. WinForms checks need an interactive
Windows client; a hosted Server CI runner is not equivalent to a Windows 10/11 VM.

For GUI-subsystem EXE output in PowerShell, pipe output (for example `| Out-String`)
and capture `$LASTEXITCODE`; a bare interactive launch may not wait as expected.
See [CLI guide](CLI.md) for supported commands, JSON and exit codes.

Real upgrades require a disposable VM/snapshot and owner authorization. Test an
app-downloaded ISO separately from a manually supplied known-good ISO. Record
actual source/index, build/edition/language, failure stage and preservation of
files/apps. Sanitize evidence; never commit raw customer logs or screenshots.

## Source recommendation is not media or upgrade validation

Keep an installed-system profile (edition, native architecture, system-default UI
language, installation type and build/revision) separate from actual WIM metadata.
Own-source eligibility and browser fallback must share the full profile across
GUI/CLI, not just an en-US test. Keep general Consumer coverage independent of
the ordered owner-source list, so adding a future source cannot change package
classification. Do not order unrelated release families by numeric build alone.

Treat an external HTML catalog as untrusted, changing input. Bound size/time,
cross-check per-table heading/build and exact filename/culture/package, allowlist
public landing URLs, and fail to manual guidance on ambiguity. Omitted HTML table
closing tags occur in the current catalog; test them. Use a separate no-redirect,
no-cookie client: checking only the final URL cannot prevent an earlier redirected
request. Do not reuse the ISO transport's cookies or disable certificate validation.
Never automate a host challenge or guess a downloaded file after browser fallback.

Server guidance must not weaken client execution guards. Manual command copy is
an explicit user effect, not an execution step: freshly revalidate exact selected
source/index and answer-file safety, quote/reject unsafe CMD paths, label the shell,
explain supported-path/licensing limits, and keep clipboard untouched on failure
or cancellation. Use fake browser/clipboard/preparation callbacks in GUI fixtures.
No recommendation or synthetic command proves a supported real Server upgrade.

## Application updates are not Windows upgrades

Read `Core/AppUpdates/`, `MainForm.AppUpdates.cs`, `CliAppUpdates.cs` and
`AppUpdateInstaller.cs` together. Discovery may run in the background, but
downloading/replacing the application always needs explicit consent. Never reuse
ISO provider cookies/redirect rules, accept an arbitrary update feed/target, replay
upgrade arguments, or authorize a Windows restart from an app-update result.

An exact release-pinned manifest/hash/size binds bytes, not a publisher identity.
Unsigned builds explicitly depend on the project GitHub/TLS trust boundary.
Preserve a valid existing signing certificate when signing is enabled; do not
silently downgrade to unsigned or disable trust checks on a network error.

The owner chose side-by-side versioned EXEs, not privileged in-place replacement
or Program Files relocation. Keep private same-folder staging, anchored ancestors,
exact executable verification and a verified successor helper. A held no-write/
no-delete executable guard must cover the final verification and launch; a path
hash followed by an unlocked CreateProcess permits swapping bytes in a writable
Desktop/Downloads folder. Never overwrite unknown existing version-named files.
CopyFile can preserve a user-writable source ACL: explicitly secure reused
candidate/helper copies before validation/execution. Existing unsafe staging ACLs
are refused, not taken over. The exact parent handle and READY/GO transfer keep
the non-thread-affine operation barrier through handoff. Never replace that
protocol with a sleep, thread-affine mutex or stale history file.

Keep app/Windows operations mutually exclusive and repeat external Setup/restart
checks after slow hashing. Failure/cancellation before handoff leaves the current
app unchanged; after handoff, keep the original EXE and diagnostic files and correlate helper status by
operation ID. Native publish/reuse/launch-lock tests use disposable files;
they do not prove signing, elevation, handoff or relaunch on a customer's VM.

Stable release packaging must verify exact product/version/source commit and
final bytes before publishing. Upload immutable assets to a draft, verify them,
then publish stable/latest last; never clobber assets or mutate a published tag.
RCs publish no update manifest. Beta has its own explicit numbered preview
channel and may publish an exact PR-CI-passed unmerged commit when the owner
authorizes that test. Stable/RC still require main ancestry. Test Beta 1 before
publishing Beta 2; do not advance stable/latest or merge merely to demonstrate
an update. Regular/critical badges and plain-text release messages never replace
consent or digital signing. Notes links point to the exact GitHub release and
must not imply download consent.

After creating a draft, inspect it by the returned immutable release ID, not only
the published-by-tag endpoint. Search authenticated release records for an existing
draft before creating anything. GitHub draft assets can temporarily use an
`untagged-*` URL: allow only the exact repository/filename before publication,
while still requiring unique IDs, uploaded state, exact sizes and SHA-256 digests.
After publication require the final pinned tag URL again. Never relax the client
download policy to compensate for publisher API behavior. If publication stops
after upload, verify the existing draft/artifacts and recover it under owner
authority; do not rerun with clobber, move tags, or replace published bytes.
Offline fixtures must cover hidden-by-tag drafts and temporary URLs, including
rejection if a temporary URL remains after publication. Run these PowerShell
packaging fixtures with PowerShell 7, as CI does (they use System.Text.Json).

## Maintain the knowledge, not just the patch

For each meaningful change, record:

1. The symptom and evidence, separating observation from hypothesis.
2. Why the chosen fix is safe and which alternatives were rejected.
3. A regression test or a reproducible safe verification procedure.
4. Remaining limits and owner field tests, with the tested commit.
5. A committed/pushed checkpoint in the same Draft PR; verify remote SHA.

Put durable lessons here, changing status in `DEVELOPMENT-STATUS.md`, user-facing
behavior in README/help/message docs, and release history in CHANGELOG/release
notes. Do not copy whole chats or depend on personal local skill installations.
Do not store temporary branch names and test dates here as permanent policy.
Never treat a green check or a recovery checkpoint as authorization to merge or
publish. Follow [GitHub workflow](GITHUB-WORKFLOW.md) and the owner's release hold.
