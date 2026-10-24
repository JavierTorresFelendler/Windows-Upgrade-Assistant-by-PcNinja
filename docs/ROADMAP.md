# Roadmap

## Current: owner-approved Beta updater trial

This section supersedes the older Program Files/first-stable test plan below.
See [portable updates](APPLICATION-UPDATES.md) for the implemented contract.
Prepare/publish Beta 1 only after safe local/CI/artifact checks; wait for the owner
to open it before publishing Beta 2. Two version-named EXEs beside each other,
explicit download/run consent, Regular/Critical summary and GitHub notes, no
original overwrite/rename and no Program Files. Beta is an isolated non-latest
preview channel. After the live demo and field acceptance, consider RC9 and
then a separately approved official V1.1. Do not merge or publish stable now.

## Current owner scope: stability, then application updates

The owner reaffirmed this scope on 2026-10-24:

- Stabilize and test the existing upgrade/download/media-validation paths. Accept
  focused fixes found by real client/VM tests; avoid unrelated feature expansion.
- Keep media extraction/copy/sanitization preparation (design PR #9) outside this
  delivery. Existing answer-file safety blocks and narrowly scoped selected-source
  cleanup remain; this deferral is not permission to weaken those safeguards.
- **Application self-update through GitHub** is implemented in development Draft
  PR #13, separately from downloading an ISO or upgrading Windows. The published
  RC8 and older binaries remain unchanged and have no updater. Stable discovery,
  explicit GUI/CLI consent, verified private staging, protected Program Files
  portable-copy installation and draft-first publication support are development
  capabilities, not a published stable feed or completed VM acceptance. See the
  [user guide](APPLICATION-UPDATES.md) and [implementation contract](design/APPLICATION-UPDATES.md).
- The remaining acceptance needs one first stable already containing the updater,
  followed by a
  higher stable version to test discovery, consent, download verification,
  recoverable replacement/relaunch and failure recovery. The second version may
  have no functional changes beyond correct release/binary version identity.
  Both stable tags/releases require separate explicit approval; neither is
  created by this PR. Verify the original outside copy, helper/gate and recovery
  on a disposable VM. Never update the app during active ISO work/Windows Setup,
  and never let app updates start Setup or authorize an OS restart.
- Credit Massgrave's alternative ISO catalogs in the README and source docs.
  Credits do not change source priority, validation or the product's licensing scope.

Historical sections below retain their original version-specific context.

## RC5 implemented requirements - owner field testing pending

The owner authorized implementation, protected-PR merge and RC5 prerelease
publication for further testing. This supersedes the earlier planning-only hold.
The requirements below are implemented with automated/local checks; actual
Windows upgrades, provider transfer and physical media still require the
[RC5 field checks](RC5-TESTING.md). Do not equate implementation with field approval.

The separately merged workflow-preparation PR kept RC4 unchanged; the subsequent
RC5 implementation includes Core, GUI/CLI and packaging changes.
Follow the [owner-led GitHub workflow](GITHUB-WORKFLOW.md).
The [RC5 work register](RC5-PLAN.md) links the issues and testing checkpoints.

### Block interfering unattended installation media (safety priority)

- Scan the selected source and other attached installation-media roots for
  interfering answer files before preparation and again immediately before Setup.
- Remove an interfering media-root answer file only when its exact location is
  established and deletion is permitted within the selected installation source;
  verify it is actually gone. Do not broadly delete OS answer files, cached
  Panther content, unrelated user files or files on other connected media.
- If an answer file remains on read-only mounted media, USB, or another source,
  block Setup. Explain briefly that customized automatic-installation media can
  trigger an unwanted clean install and data loss. Ask the user to detach that
  media (or remove the file from writable media) and refresh/recheck.
- Never continue with a warning-only bypass, auto-select a partition, or fall
  back from upgrade to clean installation. Share the gate between GUI and CLI.
- Acceptance: interfering selected or secondary ISO/USB is blocked; only the
  exact permitted writable file is removed; detaching and rechecking succeeds;
  a remaining/detected-late answer file prevents Setup from being launched.
- A future separate ISO extraction/launcher project is out of scope for this RC.

Field-test evidence reported by the owner: Windows 11 24H2 to 26H2 and Windows
10 Pro to Windows 11 Pro completed after the original VM installation media
containing an answer file was detached. This is not proof that automatic
download or all customized media are safe; those remain separate test cases.

### Two primary source choices (medium scope)

- Keep automatic download as the first choice. Merge local ISO, mounted media
  and extracted Setup folders into one **Use existing installation media** choice.
- Use one selected-source/path field and one list of discovered candidates.
  Keep lightweight ISO-file and folder browse actions; reuse Windows dialogs
  instead of adding a custom mixed file/folder picker or another UI dependency.
- Identify an explicitly chosen ISO versus directory internally, then call
  the existing shared mounting/validation operations. Preserve exact paths;
  never guess a source by newest-file order or a broad Windows filename match.
- Preserve Refresh for newly mounted media and USB drives, along with the
  selected path, download destination and restart policy.
- Update main-screen descriptions, selected-source/status text, browser-fallback
  wording, confirmation text, message catalog, offline help and documentation.
- Remove the obsolete third radio button, its panel, mutually exclusive state
  handling and redundant event handlers. Keep current CLI --iso/--mounted
  compatibility and verify both still use the same Core validation rules.
- Acceptance: two exclusive primary choices; ISO/folder/mounted/network paths
  work; refresh preserves the exact choice; cancellation remains available;
  no extra wizard pages, custom dialog framework or new package dependency.

### Reuse completed downloads and simplify filename handling

- Before downloading, inspect only the exact expected final filename at the
  selected destination. Reuse it after applicable size/checksum and media checks.
- If the file is demonstrably corrupt, delete that exact owned download, recheck
  free space and download again using the original filename.
- Cancellation, access failures, missing drive letters, external answer-file
  interference and other environmental validation failures do not prove ISO
  corruption and must not cause deletion of an otherwise valid file.
- Retain guarded partial-file resume. Do not delete files in use or unrelated
  user media. Remove the Copy N allocation method, redundant branches, obsolete
  collision tests and outdated documentation rather than leaving dead code.

### Select the installation image before launching Setup

- Enumerate actual image indexes and metadata from the selected installation
  image using the existing Windows DISM facilities, without a new UI dependency.
  Display image name, edition, index, architecture, build and language when
  available alongside the current Windows edition/language/architecture.
- Recommend the matching edition (including N/Single Language distinctions),
  but do not treat an edition-name match as proof that in-place migration works.
  For multiple eligible choices, let the user choose in the existing main-screen
  source area before the final upgrade confirmation. With one clear compatible
  choice, show the resolved selection without another wizard page.
- Pass /ImageIndex <selected-index> through the shared Core Setup command builder.
  Index numbers are specific to each source: never hard-code Pro as index 6 or
  carry an index from a previous ISO/folder to a newly selected source.
- Distinguish selecting an image INSIDE a WIM from selecting a different WIM
  FILE. If multiple source files are supported, bind the selected file and its
  index together and use /InstallFrom where applicable; split images use the
  first SWM file. Validate against the exact file Setup will consume.
- Re-enumerate/reset selection on source replacement/refresh when media changes.
  Keep /auto upgrade and the unattended-media checks; choosing an index must not
  permit clean-install fallback or arbitrary edition changes without a supported
  migration path.
- Add optional CLI --image-index, image enumeration in source validation output,
  offline help, completion and documentation. Preserve GUI/CLI Core parity and
  reject nonexistent or source-mismatched indexes before launching Setup.
- Verify multi-edition, duplicate-name, single-image, source-change and invalid
  index cases. Expected scope is small-to-medium; reuse DISM already present.

### Download, Setup and diagnostics findings to address

- Investigate Windows 10 TLS compatibility at the download redirect endpoint.
  Controlled probes failed with TLS 1.2 but reached an HTTP redirect using TLS
  1.3; the client log reports ProtocolVersion. Do not disable certificate checks.
- Wait/retry for Windows to assign a mounted ISO's drive letter; avoid reporting
  failure while a refresh immediately discovers the mounted source.
- Detect interfering answer files beyond the selected source root. Setup found
  D:\autounattend.xml while upgrading from F:\; isolate original VM install media.
  The captured SetupHost command also includes /Install /Boot alongside /auto
  upgrade. Verify the cause and fail closed if the requested upgrade cannot be
  established; never auto-select a target partition to conceal this condition.
- Reconcile Setup process exit status with post-restart OS/Setup evidence;
  one successful field upgrade was incorrectly reported failed on 0x40010004.
  Do not globally reinterpret that process status as upgrade success.

### Investigate EXE size without compromising the portable workflow

- Keep the portable EXE at or below 100,000,000 bytes; RC5 enforces the cap in
  both PR checks and release packaging. The local portable EXE is about 73 MB.

- Current settings are self-contained, single-file, compressed .NET 8 WinForms.
  The inspected publish is about 72.9 MB (69.6 MiB); the app/Core assemblies
  together are about 1.35 MB before bundle compression, including resources.
- Measure actual release outputs in the next authorized build; UI simplification
  alone will not remove the bundled runtime. Audit symbols and assets, then
  compare the existing portable build with an optional framework-dependent
  build that requires the matching Windows Desktop Runtime.
- Keep a no-runtime-install portable option unless the owner chooses otherwise.
  A smaller EXE must not conceal a runtime download or installation step.
  Avoid unsupported WinForms trimming or an architecture rewrite solely to
  reduce file size; record measured savings and the prerequisite tradeoff first.

## RC4 - download reliability and release testing

- Field-test GUI cancellation, real-provider resume, physical USB refresh and
  low-space behavior using the [RC4 test notes](RC4-TESTING.md).
- Review the short [message catalog](UI-MESSAGES.md) with the product owner.
- Complete a Windows 10 and Windows 11 upgrade from RC4-downloaded media.

## RC3 - self-documenting CLI

- Field-test global help, per-command help, the offline manual, and PowerShell
  completion from a clean machine.
- Verify mounted-drive and ISO-path completion when multiple sources exist.
- Decide whether a reviewed opt-in `$PROFILE` installer is useful after RC3.

## RC2 - standalone GUI and CLI

- Field-test automatic download through the current PcNinja endpoint.
- Prove that an automatically downloaded ISO passes validation and completes an
  in-place upgrade on clean Windows 10 and Windows 11 test machines.
- Test local ISO, mounted ISO, USB destination, low-space, removed-drive,
  interrupted-download, failed Setup, and `/noreboot` paths.
- Verify GUI layout at 100%, 125%, and 150% display scaling.
- Record Setup exit codes and rollback behavior from real machines.

## Standalone product

- Expand guarded resumable download support if the provider changes validators.
- Add an expected SHA-256 allowlist when a stable trusted source publishes one.
- Add structured support-log export.
- Add code signing before the stable release.
- Add localization after the English workflow is stable.

## Integration into PcNinja WinUpdate Tool

- Reference or package `PcNinja.Win11Upgrade.Core` as a reusable component.
- Keep the standalone application and integrated update-tool experience as
  separate products and release artifacts.
- Let WinUpdate Tool own navigation and presentation while Core owns source
  validation, ISO mounting, and Setup execution.
- Reuse PcNinja visual assets without copying the standalone WinForms screen.

## Public release

- Complete security, privacy, and licensing review.
- Review ISO distribution and endpoint terms before public promotion.
- Publish signed binaries, checksums, documentation, and limitations.
