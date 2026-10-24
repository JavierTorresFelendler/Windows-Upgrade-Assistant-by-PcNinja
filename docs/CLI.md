# Command-line guide

RC8 includes [quiet Setup and remote automation results](CLI-AUTOMATION.md),
with explicit consent, machine-readable outcomes and independent restart control.
The `app-update` commands below exist only in development Draft PR #13, not the
unchanged published RC8 binary. No new version/tag/release is created by this work.

The graphical interface opens when the EXE is started without arguments. The
same standalone EXE becomes a command-line tool when a command is supplied.

Product: **Windows Upgrade Assistant By PcNinja**, fixed EXE filename below.
Published older RC assets are not renamed. Explicit `version` and `status --json`
retain release identity; JSON status includes the complete `build` identity.
Correlated completed Windows Setup rollback is exposed as `RollbackObserved`
with `SetupFailure` result/extended codes, phase, explanation and log path.
Missing/stale logs remain unknown. No Windows files are repaired or deleted.
See [outcome diagnostics](SETUP-OUTCOME-DIAGNOSTICS.md) for correlation limits.

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe --help
.\Windows-Upgrade-Assistant-By-PcNinja.exe help upgrade
.\Windows-Upgrade-Assistant-By-PcNinja.exe help all
```

## Permissions

`status`, `downloads`, `scan`, `--help`, `--version`, and `log-path` can run normally.
Commands that mount or validate installation media, download and mount an ISO,
or start Windows Setup must run from an elevated Command Prompt or PowerShell.
Development `app-update check` and `app-update status` need no elevation;
`app-update install` requires elevation and explicit `--yes`, without UAC relaunch.

## Built-in help and PowerShell completion

The EXE contains its own documentation and does not require GitHub access:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe help
.\Windows-Upgrade-Assistant-By-PcNinja.exe help validate
.\Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --help
.\Windows-Upgrade-Assistant-By-PcNinja.exe help all
```

Enable Tab completion for the current PowerShell session:

```powershell
& .\Windows-Upgrade-Assistant-By-PcNinja.exe completion powershell |
    Out-String | Invoke-Expression
```

Completion covers commands, options relevant to the selected command, mounted
drives, extracted folders, ISO paths, and destination directories. The EXE
does not modify `$PROFILE`; run `help completion` before adding the generated
script to a persistent profile.

## Application updates (Beta trial)

Read [portable updates and live Beta acceptance](APPLICATION-UPDATES.md). Published
RC8 has no updater. Substitute the exact version-named EXE being used:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja-V1.1-Beta1.exe help app-update
.\Windows-Upgrade-Assistant-By-PcNinja-V1.1-Beta1.exe app-update check --json
.\Windows-Upgrade-Assistant-By-PcNinja-V1.1-Beta1.exe app-update status --json
# Explicit elevated consent to download/verify and stage a later version:
.\Windows-Upgrade-Assistant-By-PcNinja-V1.1-Beta1.exe app-update run --yes --json
```

check is metadata only, status is offline. Beta discovers Beta only; stable/RC
discovers stable only. Returned release metadata includes importance, message and
the exact GitHub release-notes URL. Critical does not imply download consent.

New version-named files stay beside the RUNNING EXE, not the caller's CWD.
The old file is unchanged. There is no Program Files fallback, overwrite/rename
or Copy N. Existing exact files are verified for reuse, not replaced on mismatch.
run needs Administrator and --yes, never UAC-relaunches or opens a GUI/browser.
The unreleased install verb remains an alias only.

APP_UPDATE_HANDOFF_STARTED means verified helper handoff, not completed running.
Inspect later local status, operationId and the new EXE version. Every app-update
result keeps applicationUpdated=false/setupStarted=false/readyForRestart=false.
No EXE's update result authorizes restarting Windows.

| Exit | Result codes |
| ---: | --- |
| 0 | APP_UPDATE_AVAILABLE, APP_UPDATE_CURRENT, APP_UPDATE_NO_STABLE_RELEASE, APP_UPDATE_NO_BETA_RELEASE, APP_UPDATE_STATUS, APP_UPDATE_HANDOFF_STARTED |
| 2 | INVALID_ARGUMENTS |
| 3 | APP_UPDATE_BLOCKED |
| 4 | APP_UPDATE_CHECK_UNAVAILABLE, APP_UPDATE_FAILED |
| 5 | ADMINISTRATOR_REQUIRED, APP_UPDATE_PERMISSION_DENIED |
| 6 | APP_UPDATE_CONSENT_REQUIRED, APP_UPDATE_CANCELLED |

No arbitrary feed/destination switches, Setup/restart bypass or unsigned-publisher
guarantee is offered. See the guide for local NTFS/staging/signing limitations.

## Inspect the computer and available sources

Show the Windows version, system-drive storage, administrator state, and log
path:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe status
.\Windows-Upgrade-Assistant-By-PcNinja.exe status --json
```

`status` stays offline: it reports the detected download profile and configured
PcNinja route without querying the external catalog. Existing language/status
fields remain. JSON adds `downloadProfile` (edition, language, build, revision,
architecture, installationType, isLtsc, isServer, isSupportedClient),
`automaticDownloadSourceId`, `automaticDownloadSourceName` and
`pcNinjaDownloadPage`. Reporting a Server/LTSC profile is not upgrade eligibility.

Rescan ISO files found beside the application, mounted Windows media, and
available download drives:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe scan
```

A mounted Windows ISO appears under `Mounted Windows sources`. The drive must
contain `setup.exe` at its root. If the ISO is mounted as `E:\`, verify it with:

```powershell
Test-Path E:\setup.exe
```

## Recommend a matching download page

RC8 includes the PR #21 correction: matching manually supplied x64 client LTSC media
can use `validate --iso <path>` and `upgrade --iso <path>` (or `--mounted` for an
explicit drive/folder). Inspect the actual image list and use `--image-index` when
needed. Exact edition/N/IoT and system UI language must match. No edition/channel
conversion is performed. Automatic `--download` stays unavailable for LTSC;
Server execution stays blocked.
See [manual LTSC constraints](MATCHED-ISO-DOWNLOADS.md#manual-ltsc-correction).

Request manual-download guidance for the detected system profile:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe downloads
.\Windows-Upgrade-Assistant-By-PcNinja.exe downloads --json
```

This command does not require Administrator permission. It never opens a
browser, changes the clipboard, transfers an ISO, mounts media or starts Setup.
A matching configured PcNinja source takes priority without an external catalog
request. Other supported profiles can query the public catalog using a bounded
HTML request; any landing-page human verification is left to the user.

JSON contains `recommendation` with `Source`, `PageUrl`, `FileName`,
`TargetRelease`, `TargetVersion`, `ExactMatch` and `Message`, plus
`automaticTransferStarted=false` and `setupStarted=false`. A returned recommendation
uses `resultCode=DOWNLOAD_RECOMMENDATION`, exit 0 and `readyForRestart=false`.
No exact match, an unavailable catalog or changed catalog markup can return a
manual catalog/Microsoft alternative with `ExactMatch=false`; that is guidance,
not a validated ISO or permission to upgrade. Unexpected recommendation failures
use `DOWNLOAD_FAILED` (exit 4); user cancellation uses `CANCELLED` (exit 6).

Matching is exact for system UI language, architecture and known edition/media
families; Spanish Spain and Spanish Mexico are not interchangeable. Unknown,
LTSC or unsupported profiles do not silently receive English media. A numerically
higher Windows 11 26H1 build is not treated as a general feature upgrade; current
26H1 systems have a separate same-family repair recommendation. Same-feature
media with an older revision is a repair candidate, not a newer feature release.
The actual downloaded image still needs all normal validation and safety checks.

Windows Server recommendations are download guidance only. They do not enable
Server installation/upgrade execution in the CLI or broaden its existing
host/target rules. After manually obtaining matching **supported client** media,
use `validate --iso <path>` before considering `upgrade --iso <path>`.

## Use mounted media or an extracted Setup folder

Validate the mounted media without starting Windows Setup:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe validate --mounted E:\
```

Validate it and start the upgrade:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --mounted E:\
```

Prevent Windows Setup from restarting automatically:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --mounted E:\ --no-restart
```

The drive is intentionally explicit. The assistant never guesses which mounted source to
use when more than one installation disc is present.

An extracted folder containing `setup.exe` and the `sources` directory can use
the same option:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --mounted "C:\Win11 Extracted"
```

## Use a local ISO file

Mount and validate an exact ISO path without starting Setup:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe validate --iso "D:\ISO\Windows11.iso"
```

Start an upgrade from that ISO:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --iso "D:\ISO\Windows11.iso"
```

## Download an ISO automatically

Automatic download requires a match against the **full detected system profile**:
edition, system-default UI language, architecture, client family, LTSC status and
released feature build. Language alone is not sufficient. The current configured
PcNinja OEM source is Windows 11 26H2, **en-US/x64**, for the approved Home,
Pro and Education editions, including their N/Single Language/Pro Education/
Workstation variants. Matching released Windows 10/11 clients retain this source's
priority; Enterprise, Server, LTSC, unknown/preview and ordinary 26H1 profiles do
not inherit it. An equal feature build may be a repair candidate; the configured
media revision is not independently known to be newer.

Nonmatching profiles, including en-GB, return validation exit code **3** before a
transfer. Run `downloads` or `downloads --json` for matching manual guidance; the
assistant does not switch to another automatic provider or Windows family.
`status --json` reports `systemUiLanguage`,
`automaticDownloadAvailable`, and `automaticDownloadUnavailableReason`.
Matching manually supplied client media remains available through `--iso`/
`--mounted`, subject to the existing host/target and safety rules.

Use [PcNinja ISO downloads](https://iso.pcninja.pro) for PcNinja media, or prefer
[Microsoft](https://www.microsoft.com/software-download/windows11) for standard
Microsoft media. Select the same system UI language, edition and x64 architecture.

Download to the default suitable fixed drive, validate, and start the upgrade:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --download
```

Select a download folder or removable NTFS/exFAT drive:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --download --destination "E:\PcNinja Downloads"
```

## Validation, confirmation, and automation

`validate` never starts Windows Setup. `--dry-run` provides the same safety when
using the `upgrade` command:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --iso "D:\ISO\Windows11.iso" --dry-run
```

An interactive upgrade requires typing `YES`. Automation must deliberately add
`--yes` to bypass that prompt:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --mounted E:\ --no-restart --yes
```

Add `--json` when the final result must be machine-readable. Progress and
diagnostic messages may still be written to the error stream so JSON output can
remain parseable.

## Installation image selection

`validate` lists the actual image file, all images and compatible indexes without
starting Setup. In JSON, inspect `imagePath`, `images`,
`compatibleImageIndexes` and `selectedImageIndex`.

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe validate --mounted E:\ --json
.\Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --mounted E:\ --image-index 6 --no-restart
```

Index 6 is only an example: never assume a fixed index for Pro. Without an explicit
index, exactly one matching edition/language/x64 image must exist. Windows 11
targets must be equal-or-newer builds. The manual Windows 10 -> Windows 10 POC
does not compare build order; Windows Setup decides whether apps/files can be
preserved. Windows 11 -> 10 remains blocked. Duplicate eligible images require
`--image-index`; nonexistent or
incompatible indexes are rejected. `--image-index` is accepted by `validate`,
`upgrade` and dry runs, and is listed in embedded help and option completion.
The command binds `/ImageIndex` and `/InstallFrom` to the validated file; split
images use the first `install.swm`. Image indexes are never guessed by completion.

Windows 10 uses the existing `--iso` or `--mounted` options, including WIM/ESD/SWM
media; no new flag or automatic Windows 10 downloader is introduced. Its command
is `/auto upgrade /dynamicupdate disable /compat ignorewarning`, with the exact
`/imageindex`, `/installfrom` and optional `/noreboot`. Windows 11 retains its
existing experimental command. Validation/dry-run JSON adds `setupProfile`,
`buildOrderChecked` and `policyNotice` for the unique/explicit selected image;
profile/policy are null when selection is ambiguous. Successful upgrade JSON also
reports profile/build-order policy. Eligibility is not proof that Setup will
approve migration. See [POC scope and VM checklist](WINDOWS10-POC.md).

## Answer-file safety

Selected-source and attached-media roots are checked for interfering answer files.
Only narrowly identified writable answer files within the selected installation
source can be deleted automatically. Secondary media, locked files or read-only
ISOs block the operation: detach the interfering ISO/USB or remove the file, then
retry. There is no bypass. Validation can mount an ISO and clean a selected source,
but never launches Setup. Windows-owned cached Panther files are not deleted.
Checks repeat immediately before Setup, so late-attached media can block launch.

## Cancellation, downloads and checksums

Press **Ctrl+C** during download, checksum calculation or source preparation
to request cancellation (exit code 6). Once Windows Setup has started, the
assistant does not interrupt it. `--no-restart` still controls Setup's restart
behavior independently.

Retrying `upgrade --download` resumes an incomplete file only when the server
returns a matching endpoint, strong ETag, total size and valid byte range. An
ignored Range or changed validator restarts the exact owned partial. Completed
exact-name ISOs are revalidated and reused; only proven-corrupt recorded PcNinja
downloads are deleted for replacement. Unowned user files are never overwritten
or deleted, and no numbered copies are created. Environmental failures and
cancellation preserve completed ISOs.
The last completed download is recorded by its exact name in the chosen folder,
allowing revalidation/reuse even when the provider cannot be contacted. Header
requests have a 60-second timeout; 90 seconds without data stops a transfer.
Local storage/checksum errors do not open the download website.

SHA-256 is calculated for ISO files. For independent verification, obtain an
expected hash from a trusted source and pass it explicitly:

```powershell
.\Windows-Upgrade-Assistant-By-PcNinja.exe validate --iso "D:\Windows11.iso" --sha256 <64-hex-character-hash>
.\Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --download --sha256 <64-hex-character-hash> --no-restart
```

Alternatively set `PCNINJA_ISO_SHA256` before launching the app. That setting
applies to downloaded/local ISO files in GUI and CLI. An explicit `--sha256`
takes precedence in CLI. Whole-file hashing does not apply to `--mounted` media
or folders; `--sha256 --mounted` is rejected. A locally calculated hash without
an independently trusted expected value is not proof of ISO integrity.

Repeated options, options belonging to another command, and `--destination`
without `--download` are rejected with exit code 2.

The EXE uses the Windows GUI subsystem so double-clicking has no console flash.
PowerShell invocation and redirected output remain supported. For reliable
waiting and `%ERRORLEVEL%` handling in interactive CMD, use:

```bat
start "" /wait Windows-Upgrade-Assistant-By-PcNinja.exe status --json
```

## Exit-code reference

| Code | Meaning |
| ---: | --- |
| 0 | Success |
| 1 | Unexpected error |
| 2 | Invalid arguments |
| 3 | Validation, unsupported system/storage failure, or app-update blocker |
| 4 | Download or app-update check/verification/helper failure |
| 5 | Administrator permission required |
| 6 | Confirmation or preparation cancelled |
| 7 | Windows Setup returned a failure code |
| 8 | Setup transition outcome unconfirmed; verify after restart |

The assistant never calls `shutdown.exe`. `--no-restart` adds `/noreboot` to
Windows Setup, and a failed Setup process does not trigger a forced restart.
After a successful actual `upgrade` with `--no-restart`, the CLI explicitly says
to restart manually to continue the upgrade. Successful upgrade JSON includes `manualRestartRequired`
(`true` with `--no-restart`, otherwise `false`); existing exit codes are unchanged.
`/noreboot` suppresses the first post-preparation reboot, not later Setup reboots
after the user manually restarts. PreparationCompleted is not final OS success.

## Post-restart outcome

`status --json` includes `previousUpgrade` from the persisted last-attempt record.
A zero process exit means only that Setup completed its current stage. Transition
exit `0x40010004` returns CLI code 8, not success; other nonzero Setup exits return 7.
`TargetWindowsObserved` requires the target build/edition to be running after an
observed build change with no active Setup. It does not prove files/apps were kept.
Same-build upgrades remain unconfirmed without additional evidence. No scheduled
task, startup entry, background service or forced restart is added.
Windows 10 POC attempts also require manual verification: the recorded image
build can be a shared base, not the exact installed feature release, so it never
triggers `TargetWindowsObserved` automatically.

For reliable waiting in PowerShell scripts with this GUI-subsystem EXE, capture
output through a pipeline, for example `$result = & .\Windows-Upgrade-Assistant-By-PcNinja.exe status --json | Out-String`, then read `$LASTEXITCODE`.
