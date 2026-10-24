namespace PcNinja.Win11UpgradeAssistant;

internal static class CliHelp
{
    internal static string Version => ProductIdentity.Version;

    private static readonly string[] Topics =
        ["status", "downloads", "scan", "validate", "upgrade", "app-update", "log-path", "completion"];

    public static bool TryGetRequestedTopic(string[] args, out string? topic)
    {
        topic = null;
        if (args.Length == 0) return false;

        var first = args[0].ToLowerInvariant();
        if (first is "help" or "manual")
        {
            topic = args.Length > 1 ? args[1].ToLowerInvariant() : first == "manual" ? "all" : null;
            return true;
        }

        if (first is "--help" or "-h") return true;
        if (first == "app-update" && args.Length == 3 &&
            args[1].ToLowerInvariant() is "check" or "status" or "run" or "install" && args[2] is "--help" or "-h")
        {
            topic = first;
            return true;
        }
        if (args.Length == 2 && args[1] is "--help" or "-h")
        {
            topic = first;
            return true;
        }

        return false;
    }

    public static bool TryPrint(string? topic)
    {
        topic = topic?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(topic))
        {
            Console.WriteLine(General);
            return true;
        }

        if (topic is "all" or "manual")
        {
            Console.WriteLine(General);
            foreach (var knownTopic in Topics)
            {
                Console.WriteLine();
                Console.WriteLine(GetTopic(knownTopic));
            }
            return true;
        }

        var text = GetTopic(topic);
        if (text is null) return false;
        Console.WriteLine(text);
        return true;
    }

    public static string GetPowerShellCompletion(string executableName)
    {
        var safeName = executableName.Replace("'", "''");
        return PowerShellCompletion.Replace("__EXE_NAME__", safeName, StringComparison.Ordinal);
    }

    private static string? GetTopic(string topic) => topic switch
    {
        "status" => Status,
        "downloads" => Downloads,
        "scan" or "refresh" or "rescan" => Scan,
        "validate" => Validate,
        "upgrade" => Upgrade,
        "app-update" => AppUpdate,
        "log-path" => LogPath,
        "completion" => Completion,
        _ => null
    };

    private const string General = """
Windows Upgrade Assistant By PcNinja

The graphical interface opens when the EXE is started without arguments.
Supplying a command starts CLI mode.

Usage:
  Windows-Upgrade-Assistant-By-PcNinja.exe <command> [options]
  Windows-Upgrade-Assistant-By-PcNinja.exe help <command>
  Windows-Upgrade-Assistant-By-PcNinja.exe <command> --help

Commands:
  status       Show Windows, storage, administrator, and log information.
  downloads    Recommend a matching download page without downloading an ISO.
  scan         Find local ISO files, mounted Windows media, and download drives.
  validate     Validate one exact ISO file or mounted Windows source.
  upgrade      Validate a source and start Windows Setup after confirmation.
  app-update   Check or install a stable update of this application, not Windows.
  log-path     Print the application log path.
  completion   Generate shell completion code. PowerShell completion is supported.
  help         Show general help or detailed help for one command.

Global options:
  --json          Emit machine-readable final output where supported.
  --help, -h      Show help. It can follow a command, for example upgrade --help.
  --version       Show the application version.

Learn more:
  Windows-Upgrade-Assistant-By-PcNinja.exe help validate
  Windows-Upgrade-Assistant-By-PcNinja.exe help downloads
  Windows-Upgrade-Assistant-By-PcNinja.exe help upgrade
  Windows-Upgrade-Assistant-By-PcNinja.exe help completion
  Windows-Upgrade-Assistant-By-PcNinja.exe help all

Exit codes:
  0  Success
  1  Unexpected error
  2  Invalid command or arguments
  3  Validation, unsupported system, or storage failure
  4  Automatic download failure
  5  Administrator permission required
  6  Confirmation cancelled
  7  Windows Setup returned a failure code
  8  Setup transition outcome is unconfirmed; verify after restart

Mounting, validation, and upgrade commands must be run as Administrator.
The assistant never calls shutdown.exe and never forces a restart after failure.
JSON results include schemaVersion, resultCode, exitCode, message and readyForRestart.
Exit 0 alone is NOT permission to restart: status, validate and dry-run also return 0.
See help upgrade for the remote-script/management-agent result contract.
""";

    private const string Status = """
COMMAND: status

Show the detected Windows version, whether upgrades are supported, available
space on the Windows drive, administrator state, log path, and the previous
upgrade attempt. TargetWindowsObserved confirms only the running build/edition,
not preservation of personal files or applications.
Also reports the detected edition, language, architecture, build/revision and
client/Server/LTSC profile, automatic-download availability/reason and its PcNinja
browser route. Existing language/status fields are retained. Status stays offline;
it does not query the external download catalog. Use downloads for a recommendation.
JSON includes the complete build identity. RollbackObserved and SetupFailure
report a correlated completed Windows Setup rollback, including its result and
extended codes. Missing, stale or incomplete logs do not prove success/failure.

Usage:
  Windows-Upgrade-Assistant-By-PcNinja.exe status
  Windows-Upgrade-Assistant-By-PcNinja.exe status --json

This command does not require Administrator permission and can report an
unsupported Windows Server/architecture/type without attempting an upgrade.
Matching LTSC client media can use the manual upgrade path; automatic download
remains unavailable for LTSC. Host eligibility is not image or Setup compatibility.
""";

    private const string Downloads = """
COMMAND: downloads

Recommend a download page for the detected edition, language, architecture and
build/revision without downloading an ISO or starting Windows Setup.

Usage:
  Windows-Upgrade-Assistant-By-PcNinja.exe downloads
  Windows-Upgrade-Assistant-By-PcNinja.exe downloads --json

Matching PcNinja automatic-source profiles use their configured browser page
without contacting an external catalog. Other profiles can query the online
catalog for matching manual-download guidance. A recommendation is not proof
of ISO contents or upgrade compatibility: validate the actual downloaded media.
The command never opens a browser, changes the clipboard, mounts media or
starts a transfer/Setup, and does not require Administrator permission.

JSON includes recommendation (Source, PageUrl, FileName, TargetRelease,
TargetVersion, ExactMatch and Message), automaticTransferStarted=false and
setupStarted=false. Result: DOWNLOAD_RECOMMENDATION, exit 0, readyForRestart=false.
Unavailable/changed catalogs return manual guidance, exit 0 and ExactMatch=false.
Unrecoverable resolution errors return DOWNLOAD_FAILED (exit 4); cancellation
returns CANCELLED (exit 6). None authorizes a restart.

After downloading matching media manually:
  Windows-Upgrade-Assistant-By-PcNinja.exe validate --iso "D:\ISO\Matching.iso"
  Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --iso "D:\ISO\Matching.iso"

This does not broaden the standalone upgrader's supported system/target rules.
""";

    private const string Scan = """
COMMAND: scan

Rescan sources without changing the computer. Aliases: refresh, rescan.

Usage:
  Windows-Upgrade-Assistant-By-PcNinja.exe scan
  Windows-Upgrade-Assistant-By-PcNinja.exe scan --json

The output contains:
  Local ISO files          Suitable .iso files found beside the application.
  Mounted Windows sources Drives containing setup.exe at their root.
  Download locations      Ready fixed or removable drives and free space.

Example workflow for a mounted ISO:
  Windows-Upgrade-Assistant-By-PcNinja.exe scan
  Windows-Upgrade-Assistant-By-PcNinja.exe validate --mounted E:\
  Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --mounted E:\ --no-restart

This command does not require Administrator permission.
""";

    private const string Validate = """
COMMAND: validate

Validate exactly one installation source without starting Windows Setup.

Usage:
  Windows-Upgrade-Assistant-By-PcNinja.exe validate --iso <exact-iso-path> [--sha256 <hash>] [--image-index <index>] [--json]
  Windows-Upgrade-Assistant-By-PcNinja.exe validate --mounted <drive-or-folder> [--image-index <index>] [--json]

Examples:
  Windows-Upgrade-Assistant-By-PcNinja.exe validate --iso "D:\ISO\Windows11.iso"
  Windows-Upgrade-Assistant-By-PcNinja.exe validate --mounted E:\
  Windows-Upgrade-Assistant-By-PcNinja.exe validate --mounted "C:\Win11 Extracted"
  Windows-Upgrade-Assistant-By-PcNinja.exe validate --mounted E:\ --json

--iso mounts the exact file, then validates the mounted media.
--mounted accepts either an ISO that Windows already mounted or an extracted
Windows Setup folder containing setup.exe and the sources directory.

Validation checks the system drive, ISO size, setup.exe location, Microsoft
signature, interfering answer files on selected and attached media, and image
metadata in install.wim, install.esd or the first install.swm. A narrowly scoped
writable answer file in the selected source is removed; otherwise Setup is
blocked. Other connected media and Windows-owned files are never auto-deleted.
Validation may mount the ISO and clean the selected source; it never runs Setup.
Administrator permission is required.

Image indexes, names, editions, architecture, build and default language are
listed, including compatible indexes. --image-index validates an explicit index.
Index numbers belong to this exact image file, not to a universal edition number.
ISO SHA-256 is calculated; --sha256 or PCNINJA_ISO_SHA256 supplies a trusted
expected hash for comparison. --sha256 cannot be used with --mounted.
""";

    private const string Upgrade = """
COMMAND: upgrade

Validate exactly one source and start the known-good Windows Setup command.

Source modes - choose exactly one:
  --iso <path>             Use and mount one exact local ISO file.
  --mounted <path>         Use mounted media or an extracted Setup folder.
  --download               Download an ISO automatically, then mount it.

Additional options:
  --destination <folder>   Download folder for --download. NTFS/exFAT required.
  --sha256 <hash>          Compare an ISO with a trusted 64-character SHA-256.
  --image-index <index>    Select an image in the validated source; positive integer.
  --no-restart             Add /noreboot; restart manually when ready.
  --quiet                  Add /quiet; requires --yes unless --dry-run. No browser fallback.
  --dry-run                Perform full validation but do not start Setup.
  --yes                    Explicitly skip the interactive YES confirmation.
  --json                   Emit a machine-readable final result.

Examples:
  Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --mounted E:\
  Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --mounted "C:\Win11 Extracted"
  Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --mounted E:\ --no-restart
  Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --iso "D:\ISO\Windows11.iso" --dry-run
  Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --iso "D:\ISO\Windows11.iso" --no-restart
  Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --download --destination "F:\PcNinja Downloads"
  Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --mounted E:\ --no-restart --yes --json
  Windows-Upgrade-Assistant-By-PcNinja.exe upgrade --mounted E:\ --quiet --no-restart --yes --json

The same-edition, x64, system-UI-language image is selected automatically only
when exactly one match exists. Windows 11 targets must be equal-or-newer builds.
For Windows 10 -> Windows 10, this POC does NOT compare build order: Setup must
decide whether an in-place upgrade/repair keeping apps and files is available.
Windows 11 -> Windows 10 remains blocked. Windows 10 targets require manually
supplied --iso or --mounted media; --download remains Windows 11 only.
Validation/dry-run JSON reports setupProfile and buildOrderChecked (null if no
unique/explicit image was selected). These are policy details, not proof of
migration support. Otherwise specify
--image-index after inspecting validate output. An explicit incompatible image
is rejected; there is no clean-install or arbitrary edition-change fallback.

Without --yes, type YES at the final safety prompt. Non-interactive execution
without --yes is cancelled. Administrator permission is required.

Windows 11 target arguments (existing experimental profile):
  /product server /auto upgrade /eula accept /dynamicupdate disable
  /compat ignorewarning /imageindex <selected-index> /installfrom "<image-file>"

Windows 10 target arguments:
  /auto upgrade /dynamicupdate disable /compat ignorewarning
  /imageindex <selected-index> /installfrom "<image-file>"
No /product server or /eula accept for Windows 10. /quiet is added only with
explicit --quiet. No clean or data-only fallback is added. A refused migration
must stop, not switch installation type.

--no-restart additionally supplies /noreboot. PcNinja never calls shutdown.exe.
After Setup exit 0 with --no-restart, restart manually to continue the upgrade.
Successful upgrade JSON includes manualRestartRequired. /noreboot suppresses
the first post-preparation reboot, not later Setup reboots after manual restart.
PreparationCompleted does not confirm a finished OS migration.

--quiet hides Windows Setup UI, not diagnostic results or safety checks. It does
not imply --yes and does not change restart policy in CLI: add --no-restart for
external orchestration. No CLI error popups are shown, including remote/SYSTEM use.
Quiet mode suppresses progress and never opens a fallback browser.

Remote scripts/management agents (including BigFix): use --quiet --no-restart
--yes --json, wait for this process to exit, and capture stdout and exit code.
JSON schemaVersion 1 preserves prior fields and adds stable resultCode/message,
readyForRestart, exitCode, logPath and build. Setup results include the original
signed setupExitCode and setupExitCodeHex. Result identifiers:
  UPGRADE_PREPARED_RESTART_REQUIRED   Exit 0; preparation complete, restart needed.
  UPGRADE_PREPARED_AUTOMATIC_RESTART  Exit 0; Windows handles restart; do not force one.
  SETUP_FAILED                      Exit 7; native Setup error; do not restart.
  SETUP_OUTCOME_UNCONFIRMED          Exit 8; uncertain transition; do not auto-restart.
  VALIDATION_PASSED / DRY_RUN_COMPLETED / STATUS_REPORTED / SOURCES_SCANNED
  DOWNLOAD_RECOMMENDATION
                                    Exit 0, but readyForRestart=false.
Other failures retain exit codes 1-6 and return readyForRestart=false.
Only authorize a controller-managed restart after the actual upgrade returns
exit 0, resultCode UPGRADE_PREPARED_RESTART_REQUIRED, outcome PreparationCompleted,
setupExitCode 0 and readyForRestart=true. JSON/transport/timeout failure is NOT success.
Text results include RESULT_CODE markers; diagnostics also go to the app log.

Ctrl+C cancels download, checksum and preparation work (exit code 6). It does
not interrupt Windows Setup after launch. Network failures may open the normal
browser fallback in interactive mode; disk errors never do.
Downloads stop after 90 seconds without incoming data. Retry resumes only the
exact original-name partial with matching endpoint, strong ETag, size and range.
An ignored Range restarts that owned partial; Copy N files are no longer created.
An existing exact-name completed ISO is reused after integrity/media validation.
Only a proven-corrupt, recorded PcNinja download is deleted automatically;
cancellation, media safety and environmental errors preserve the existing ISO.
The original filename is required; files are never guessed by a wildcard.
A short-link TLS connection failure triggers a secure direct-provider attempt;
provider blocks may still require the normal browser fallback.
Automatic transfer is available only when the full detected system profile
matches a configured PcNinja source: edition, language, architecture, client
family, LTSC status and build eligibility all matter, not language alone.
The configured Windows 11 source is en-US; other profiles (including en-GB)
must use matching manual media with --iso or --mounted. --download is rejected
with exit code 3 rather than downloading a mismatched ISO. Run downloads or
downloads --json for matching manual-download guidance. This command does not
silently change the automatic source or start a different Windows-family upgrade.
Manual downloads: https://iso.pcninja.pro or
https://www.microsoft.com/software-download/windows11
For standard Microsoft Windows media, prefer Microsoft's download page.
SHA-256 is calculated for ISO files. Supply --sha256 or PCNINJA_ISO_SHA256 to
compare with a trusted value; a calculated hash alone is not proof of integrity.
""";

    private const string AppUpdate = """
COMMAND: app-update

Download and run a new portable assistant, NOT a Windows update or installer.

Usage (substitute the exact version-named EXE you are running):
  Windows-Upgrade-Assistant-By-PcNinja-V1.1-Beta1.exe app-update check [--json]
  Windows-Upgrade-Assistant-By-PcNinja-V1.1-Beta1.exe app-update status [--json]
  Windows-Upgrade-Assistant-By-PcNinja-V1.1-Beta1.exe app-update run --yes [--json]

check reads only pinned GitHub release metadata/manifest. Beta executables select
numbered Beta releases; stable/RC executables select stable only. Drafts, other
repositories and older/equal versions are not offered. status stays offline.

run requires Administrator and explicit --yes. The exact version-named EXE is
verified and saved BESIDE THE RUNNING EXE, not the current shell directory.
No Program Files relocation, original replacement/rename or numbered Copy N.
An existing exact-name file is validated for reuse; a mismatch is not overwritten.
Unsupported/read-only folders fail rather than silently choosing another location.
The install verb remains an alias for run; no software is installed.

GUI shows Regular update or Critical update, publisher summary and exact GitHub
release notes, with Download and run the new version / Not now choices. Critical
does NOT authorize an automatic download. CLI emits these metadata in --json.

Bytes, size, SHA-256, x64 GUI/product/version and signed-publisher continuity are
verified. Unsigned trust depends on the publishing GitHub account/manifest; a
checksum or critical tag is not an independent digital signature.

A verified helper waits for this exact parent to exit. GUI reopens the new EXE
with no arguments; CLI never opens a GUI or replays commands. The old file stays.
APP_UPDATE_HANDOFF_STARTED means handed off, NOT running yet. Inspect subsequent
app-update status and the new copy's --version. readyForRestart and setupStarted
are always false. No Windows upgrade/reboot is started.

Exit groups: 0 result/handoff, 2 invalid arguments, 3 blocked, 4 check/verification/
helper failure, 5 permission, 6 cancelled. Active Windows operations, external
Setup or pending Windows restart block running an update; no force bypass.
""";

    private const string LogPath = """
COMMAND: log-path

Print the full path of the PcNinja upgrade log.

Usage:
  Windows-Upgrade-Assistant-By-PcNinja.exe log-path

This command does not require Administrator permission.
""";

    private const string Completion = """
COMMAND: completion

Generate a PowerShell native argument completer for the current EXE name.

Enable completion for the current PowerShell session:
  & .\Windows-Upgrade-Assistant-By-PcNinja.exe completion powershell |
    Out-String | Invoke-Expression

After registration, press Tab to complete commands, relevant options, mounted
drives containing setup.exe, ISO paths, and destination directories.

The command prints a script only. It does not modify $PROFILE automatically.
To make it persistent, review the generated script and add it to your profile.
PowerShell completion is supported; CMD receives the embedded help only.
""";

    private const string PowerShellCompletion = """
# Windows Upgrade Assistant By PcNinja native completion
Register-ArgumentCompleter -Native -CommandName '__EXE_NAME__', 'Windows-Upgrade-Assistant-By-PcNinja.exe' -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)

    $elements = @($commandAst.CommandElements | ForEach-Object { $_.Extent.Text.Trim("'").Trim('"') })
    $command = if ($elements.Count -gt 1) { $elements[1].ToLowerInvariant() } else { '' }
    $previousIndex = if ([string]::IsNullOrWhiteSpace($wordToComplete)) { $elements.Count - 1 } else { $elements.Count - 2 }
    $previous = if ($previousIndex -ge 1) { $elements[$previousIndex].ToLowerInvariant() } else { '' }

    if ($previous -eq '--image-index') { return } # Validate lists the actual indexes; do not guess.

    if ($previous -eq '--mounted') {
        Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.Root 'setup.exe') } |
            ForEach-Object {
                [System.Management.Automation.CompletionResult]::new($_.Root, $_.Root, 'ParameterValue', 'Mounted Windows source')
            }
        $filter = if ([string]::IsNullOrWhiteSpace($wordToComplete)) { '*' } else { "$wordToComplete*" }
        Get-ChildItem -Path $filter -Directory -Force -ErrorAction SilentlyContinue |
            ForEach-Object {
                $value = $_.FullName
                $completion = if ($value.Contains(' ')) { "'" + $value.Replace("'", "''") + "'" } else { $value }
                [System.Management.Automation.CompletionResult]::new($completion, $_.Name, 'ParameterValue', $value)
            }
        return
    }

    if ($previous -in @('--iso', '--destination')) {
        $filter = if ([string]::IsNullOrWhiteSpace($wordToComplete)) { '*' } else { "$wordToComplete*" }
        Get-ChildItem -Path $filter -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.PSIsContainer -or ($previous -eq '--iso' -and $_.Extension -eq '.iso') } |
            ForEach-Object {
                $value = $_.FullName
                $completion = if ($value.Contains(' ')) { "'" + $value.Replace("'", "''") + "'" } else { $value }
                [System.Management.Automation.CompletionResult]::new($completion, $_.Name, 'ParameterValue', $value)
            }
        return
    }

    $commands = @('status', 'downloads', 'scan', 'validate', 'upgrade', 'app-update', 'log-path', 'completion', 'help', '--help', '--version')
    $options = switch ($command) {
        'status'     { @('--json', '--help') }
        'downloads'  { @('--json', '--help') }
        'scan'       { @('--json', '--help') }
        'refresh'    { @('--json', '--help') }
        'rescan'     { @('--json', '--help') }
        'validate'   { @('--iso', '--mounted', '--sha256', '--image-index', '--json', '--help') }
        'upgrade'    { @('--iso', '--mounted', '--download', '--destination', '--sha256', '--image-index', '--no-restart', '--quiet', '--dry-run', '--yes', '--json', '--help') }
        'app-update' { @('check', 'status', 'run', '--yes', '--json', '--help') }
        'completion' { @('powershell', '--help') }
        'help'       { @('status', 'downloads', 'scan', 'validate', 'upgrade', 'app-update', 'log-path', 'completion', 'all') }
        default      { $commands }
    }

    $options |
        Where-Object { $_ -like "$wordToComplete*" } |
        Sort-Object -Unique |
        ForEach-Object {
            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
        }
}
""";
}
