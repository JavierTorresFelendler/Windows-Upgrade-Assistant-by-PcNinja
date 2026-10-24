# Quiet Setup and remote automation results

The quiet Setup/result contract is included in RC8. Application-update results
below apply only to development Draft PR #13, not the unchanged RC8 binary.
Neither interface implements agent-controlled restart code.

## Invocation

Run elevated, wait for the process to finish, capture its stdout and process exit
code. For example, with an exact extracted or mounted Windows source:

```powershell
$exe = '.\Windows-Upgrade-Assistant-By-PcNinja.exe'
$output = & $exe upgrade --mounted 'D:\WindowsMedia' --quiet --no-restart --yes --json | Out-String
$assistantExitCode = $LASTEXITCODE
$result = $output | ConvertFrom-Json -ErrorAction Stop
$ready = $assistantExitCode -eq 0 -and
    $result.schemaVersion -eq 1 -and
    $result.resultCode -eq 'UPGRADE_PREPARED_RESTART_REQUIRED' -and
    $result.outcome -eq 'PreparationCompleted' -and
    $result.setupExitCode -eq 0 -and
    $result.readyForRestart -eq $true

# Record the JSON and exit code in your management system/script log.
# Only $ready authorizes the calling tool's separately controlled restart.
# This example deliberately does not restart the computer.
$output
"CONTROLLER_READY_FOR_RESTART=$ready"
exit $assistantExitCode
```

For an ISO use `--iso '<exact ISO path>'`; image selection, validation and safety
gates remain the same. `--image-index` still belongs to the exact validated media.
No wildcard/newest-file selection or clean-install fallback is introduced.

`--quiet` maps to Windows Setup `/quiet`. It does not imply acceptance: a real
quiet upgrade requires explicit `--yes`; otherwise exit 6 occurs before media
access/download. `--dry-run` can inspect media without consent, never runs Setup
and never authorizes restart. CLI quiet/restart are independent: add `--no-restart`
to pass `/noreboot`. No agent-specific reboot code such as 3010 is returned.

In GUI only, **Run Windows Setup silently** selects and disables automatic restart;
unchecking restores the previous restart selection. Initial upgrade consent still
applies and explains silent Setup. Error/status handling remains in the assistant.
CLI has no error popup, no UAC relaunch and no GUI restart notice. Quiet CLI never
opens a fallback browser or waits for a hidden interactive YES prompt.

## Machine-readable contract

`--json` writes one final JSON object to stdout. Existing fields and exit codes
0-8 are preserved; these fields are additive:

| Field | Meaning |
| --- | --- |
| schemaVersion | Contract version, currently 1 |
| resultCode | Stable identifier; do not parse human error text |
| success / exitCode | Assistant command outcome and process exit code |
| message | Human-readable description; failure also keeps the existing error field |
| readyForRestart | True only after actual successful upgrade preparation with restart disabled |
| setupExitCode / setupExitCodeHex | Original signed Setup process code and unsigned eight-digit hex; null when not available |
| logPath / version / build | Support log and exact assistant build identity |

| Process exit | resultCode | Controller action |
| --- | --- | --- |
| 0 | UPGRADE_PREPARED_RESTART_REQUIRED | Preparation completed; readyForRestart=true; controller can schedule restart |
| 0 | UPGRADE_PREPARED_AUTOMATIC_RESTART | Windows handles restart; readyForRestart=false |
| 0 | VALIDATION_PASSED / DRY_RUN_COMPLETED / STATUS_REPORTED / SOURCES_SCANNED | No restart authorization |
| 0 | DOWNLOAD_RECOMMENDATION / APP_UPDATE_* | Guidance/app-only result; never restart authorization |
| 1 | UNEXPECTED_ERROR | Record details; do not restart |
| 2 | INVALID_ARGUMENTS | Correct invocation; do not restart |
| 3 | VALIDATION_FAILED | Resolve source/system/storage blocker; do not restart |
| 4 | DOWNLOAD_FAILED | Supply/retry valid media; do not restart |
| 5 | ADMINISTRATOR_REQUIRED | Rerun elevated; do not restart |
| 6 | CANCELLED | Includes missing explicit quiet consent; do not restart |
| 7 | SETUP_FAILED | Inspect native Setup code/logs; do not restart |
| 8 | SETUP_OUTCOME_UNCONFIRMED | Uncertain transition; no automatic restart authorization |

For example, Setup code `0xC190020E` is retained as that hex value and its signed
integer, while the assistant returns process exit 7 and SETUP_FAILED. This is
different from a prelaunch storage check, which returns exit 3/VALIDATION_FAILED.
All failures have readyForRestart=false. Plain-text results contain RESULT_CODE
markers; failure text stays on stderr. The app log also records result identifiers.
Version/help/log-path remain their existing plain-text commands.

## BigFix and other remote callers

The result contract is agent-neutral: capture output and native exit code in
BigFix or another runner. The caller must wait for this EXE, not just launch it.
If wrapping it in PowerShell, propagate the captured exit code explicitly as
above; successful output/log-writing commands must not mask a failed EXE.
Do not use exit 0 alone: status, validation and dry runs also succeed. Parse/transport
failure, killed process or management-agent timeout never authorizes restart.
The assistant does not configure BigFix, call shutdown.exe or restart a failed
machine. Live BigFix/SYSTEM integration and real quiet upgrades require separate
owner testing; synthetic fixtures do not prove remote agent wiring.

`PreparationCompleted` is completion of the current pre-reboot stage, not proof
that the OS migration will succeed after reboot. `/noreboot` suppresses only the
first reboot; Windows can restart again during subsequent installation phases.
Windows 11 retains `/eula accept`; Windows 10 does not gain that unsupported flag.
All edition/language/index/space/signature/unsafe-media checks remain in Core.

## Application-update automation (Beta trial)

Use app-update check/status/run, never upgrade, to update the portable tool.
check reads metadata only; status stays offline. Explicit elevated
`app-update run --yes --json` consents to transfer/staging, not Windows actions.
The unreleased install verb remains an alias. CLI never opens a GUI/browser,
replays upgrade arguments or asks for hidden input.

New version-named EXEs stay beside the current EXE; no Program Files relocation,
original overwrite/rename or numbered copies. An existing exact file is verified
for reuse; unsafe/mismatched locations fail rather than silently relocating.
Beta and stable channels remain isolated. JSON release includes importance,
message and exact GitHub notes URL. Critical never grants implicit consent.

Capture stdout and process exit through a pipeline. APP_UPDATE_HANDOFF_STARTED
means helper handoff, not successful run: correlate subsequent operationId/local
status and actual new EXE --version. Every updater result retains
applicationUpdated=false, setupStarted=false and readyForRestart=false. An
updater result never authorizes an OS restart. Agent/live helper acceptance is
distinct from mocks/CI. See [application updates](APPLICATION-UPDATES.md).

Reference: [Microsoft Windows Setup options: Quiet, NoReboot and EULA](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/windows-setup-command-line-options).
