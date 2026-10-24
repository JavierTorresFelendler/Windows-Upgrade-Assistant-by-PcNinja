param(
    [Parameter(Mandatory)][string]$ExePath,
    [Parameter(Mandatory)][string]$ExpectedVersion
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $ExePath).Path
function Invoke-Assistant([string[]]$Arguments) {
    $text = & $exe @Arguments | Out-String
    $script:LastCode = $LASTEXITCODE
    return $text
}
$version = Invoke-Assistant @('--version')
if ($LastCode -ne 0 -or $version.Trim() -ne $ExpectedVersion) { throw 'Version mismatch.' }
$help = Invoke-Assistant @('help', 'all')
if ($LastCode -ne 0) { throw 'Offline help failed.' }
if (-not $help.Contains('manualRestartRequired') -or -not $help.Contains('restart manually to continue')) {
    throw 'Offline help omitted successful manual-restart guidance.'
}
if (-not $help.Contains('Windows 10 -> Windows 10') -or -not $help.Contains('does NOT compare build order') -or
    -not $help.Contains('Windows 10 target arguments:') -or -not $help.Contains('buildOrderChecked')) {
    throw 'Offline help omitted the Windows 10 POC profile/policy.'
}
foreach ($option in @('--mounted', '--iso', '--download', '--image-index', '--sha256', '--no-restart', '--quiet', '--dry-run', '--yes', '--json')) {
    if (-not $help.Contains($option)) { throw "Offline help missing $option" }
}
$status = (Invoke-Assistant @('status', '--json')) | ConvertFrom-Json
if ($LastCode -ne 0 -or -not $status.operatingSystem -or -not $status.version) { throw 'Status JSON failed.' }
if ($status.schemaVersion -ne 1 -or $status.resultCode -ne 'STATUS_REPORTED' -or
    $status.readyForRestart -ne $false -or $status.exitCode -ne 0) { throw 'Status result could authorize a restart or lacks the automation contract.' }
if (-not $status.build -or -not $status.build.StartsWith($ExpectedVersion)) { throw 'Build provenance missing.' }
if (-not $help.Contains('RollbackObserved') -or -not $help.Contains('SetupFailure')) { throw 'Rollback help missing.' }
if (-not $help.Contains('COMMAND: app-update') -or -not $help.Contains('APP_UPDATE_HANDOFF_STARTED') -or
    -not $help.Contains('NOT running yet')) { throw 'Offline application-update contract missing.' }
$appHelp = Invoke-Assistant @('app-update', 'run', '--help')
if ($LastCode -ne 0 -or -not $appHelp.Contains('COMMAND: app-update')) { throw 'App-update subcommand help failed.' }
# These updater probes MUST stay offline and stop before any installation effect.
$appStatus = (Invoke-Assistant @('app-update', 'status', '--json')) | ConvertFrom-Json
if ($LastCode -ne 0 -or $appStatus.resultCode -ne 'APP_UPDATE_STATUS' -or
    $appStatus.readyForRestart -ne $false -or $appStatus.setupStarted -ne $false -or $appStatus.applicationUpdated -ne $false) {
    throw 'App-update status lost its offline/app-only result contract.'
}
$appDeclined = (Invoke-Assistant @('app-update', 'run', '--json')) | ConvertFrom-Json
if ($LastCode -ne 6 -or $appDeclined.resultCode -ne 'APP_UPDATE_CONSENT_REQUIRED' -or
    $appDeclined.readyForRestart -ne $false -or $appDeclined.setupStarted -ne $false) {
    throw 'App update without consent did not stop before network/install.'
}
if ((Get-Item -LiteralPath $exe).VersionInfo.ProductName -ne 'Windows Upgrade Assistant By PcNinja') {
    throw 'Product metadata branding mismatch.'
}
if (-not $status.systemUiLanguage -or $null -eq $status.automaticDownloadAvailable) { throw 'Language availability missing from status.' }
$profile = $status.downloadProfile
if (-not $profile -or -not $profile.edition -or -not $profile.architecture -or -not $profile.installationType -or
    $profile.language -ine $status.systemUiLanguage -or $null -eq $profile.build -or $null -eq $profile.revision -or
    $null -eq $profile.isServer -or $null -eq $profile.isLtsc -or $null -eq $profile.isSupportedClient) {
    throw 'Full offline download profile missing from status.'
}
$automaticExpected = -not [string]::IsNullOrWhiteSpace([string]$status.automaticDownloadSourceId)
if ($status.automaticDownloadAvailable -ne $automaticExpected -or
    ($automaticExpected -and -not $status.automaticDownloadSourceName)) {
    throw 'Automatic availability disagrees with the matched configured source.'
}
if (-not $automaticExpected -and -not $status.automaticDownloadUnavailableReason) {
    throw 'Unavailable automatic download omitted its full-profile reason.'
}
if (-not $status.pcNinjaDownloadPage -or -not $status.pcNinjaDownloadPage.StartsWith('https://')) {
    throw 'Offline status omitted its PcNinja browser route.'
}
if (-not $help.Contains('en-US') -or -not $help.Contains('https://iso.pcninja.pro')) { throw 'Offline help omitted the language policy/manual catalog.' }
if (-not $help.Contains('COMMAND: downloads') -or -not $help.Contains('DOWNLOAD_RECOMMENDATION') -or
    -not $help.Contains('Status stays offline') -or -not $help.Contains('changes the clipboard')) {
    throw 'Offline help omitted the read-only matching-download contract.'
}
# Only invoke downloads on a separately recognized own-source host. Other
# profiles may query the live catalog; this verification must not do that.
$knownOwnEditions = @('Core', 'CoreN', 'CoreSingleLanguage', 'Education', 'EducationN',
    'Professional', 'ProfessionalN', 'ProfessionalEducation', 'ProfessionalEducationN',
    'ProfessionalWorkstation', 'ProfessionalWorkstationN')
$knownOwnProfile = $profile.language -ieq 'en-US' -and $profile.architecture -ieq 'x64' -and
    $profile.installationType -ieq 'Client' -and -not $profile.isServer -and -not $profile.isLtsc -and
    $profile.isSupportedClient -and $profile.edition -in $knownOwnEditions -and
    $profile.build -ge 10240 -and $profile.build -le 26300
if ($automaticExpected -and $knownOwnProfile) {
    $recommendation = (Invoke-Assistant @('downloads', '--json')) | ConvertFrom-Json
    if ($LastCode -ne 0 -or $recommendation.resultCode -ne 'DOWNLOAD_RECOMMENDATION' -or
        $recommendation.readyForRestart -ne $false -or $recommendation.automaticTransferStarted -ne $false -or
        $recommendation.setupStarted -ne $false -or -not $recommendation.recommendation.PageUrl -or
        $recommendation.recommendation.PageUrl.TrimEnd('/') -ine $status.pcNinjaDownloadPage.TrimEnd('/')) {
        throw 'Own-source read-only download recommendation failed.'
    }
} else {
    Write-Output 'SKIP: downloads live command on a non-own-source profile; synthetic catalog presenter checks remain separate.'
}
$scan = (Invoke-Assistant @('scan', '--json')) | ConvertFrom-Json
if ($LastCode -ne 0 -or $null -eq $scan.localIsos -or $null -eq $scan.mountedSources) { throw 'Scan JSON failed.' }
if ($scan.resultCode -ne 'SOURCES_SCANNED' -or $scan.readyForRestart -ne $false) { throw 'Scan result could authorize a restart.' }
foreach ($case in @(
    @('status', '--no-restart', '--json'),
    @('status', '--quiet', '--json'),
    @('downloads', '--download', '--json'),
    @('downloads', '--quiet', '--json'),
    @('downloads', '--yes', '--json'),
    @('validate', '--mounted', 'Z:\not-used', '--quiet', '--json'),
    @('upgrade', '--quiet', '--quiet', '--yes', '--json'),
    @('status', '--image-index', '1', '--json'),
    @('app-update', 'run', '--yes', '--download', '--json'),
    @('app-update', 'check', '--yes', '--json'),
    @('upgrade', '--iso', 'first.iso', '--iso', 'second.iso', '--json'),
    @('upgrade', '--iso', 'not-used.iso', '--image-index', '0', '--json'),
    @('validate', '--mounted', 'Z:\not-used', '--image-index', '-1', '--json'),
    @('validate', '--mounted', 'Z:\not-used', '--image-index', 'abc', '--json'),
    @('upgrade', '--mounted', 'Z:\not-used', '--destination', 'Z:\not-used', '--json')
)) {
    $errorResult = (Invoke-Assistant $case) | ConvertFrom-Json
    if ($LastCode -ne 2 -or $errorResult.exitCode -ne 2) { throw "Invalid arguments accepted: $case" }
    if ($errorResult.resultCode -ne 'INVALID_ARGUMENTS' -or $errorResult.readyForRestart -ne $false) {
        throw 'Invalid argument result lost its machine-readable identifier.'
    }
}
$quietDeclined = (Invoke-Assistant @('upgrade', '--mounted', 'Z:\not-used', '--quiet', '--json')) | ConvertFrom-Json
if ($LastCode -ne 6 -or $quietDeclined.resultCode -ne 'CANCELLED' -or $quietDeclined.readyForRestart -ne $false) {
    throw 'Quiet without explicit consent did not stop before transfer/Setup.'
}
$completion = Invoke-Assistant @('completion', 'powershell')
if ($LastCode -ne 0 -or -not $completion.Contains('--image-index') -or -not $completion.Contains("'downloads'") -or
    -not $completion.Contains("'app-update'")) { throw 'Completion options missing.' }
[scriptblock]::Create($completion) | Out-Null
Invoke-Expression $completion
$inputLine = "& '$($exe.Replace("'", "''"))' upgrade --image-"
$matches = [System.Management.Automation.CommandCompletion]::CompleteInput($inputLine, $inputLine.Length, $null)
if ('--image-index' -notin $matches.CompletionMatches.CompletionText) { throw 'Native Tab completion failed.' }
$quietLine = "& '$($exe.Replace("'", "''"))' upgrade --qui"
$quietMatches = [System.Management.Automation.CommandCompletion]::CompleteInput($quietLine, $quietLine.Length, $null)
if ('--quiet' -notin $quietMatches.CompletionMatches.CompletionText) { throw 'Quiet Tab completion failed.' }
$downloadsLine = "& '$($exe.Replace("'", "''"))' downloads --j"
$downloadsMatches = [System.Management.Automation.CommandCompletion]::CompleteInput($downloadsLine, $downloadsLine.Length, $null)
if ('--json' -notin $downloadsMatches.CompletionMatches.CompletionText) { throw 'Download guidance Tab completion failed.' }
$appLine = "& '$($exe.Replace("'", "''"))' app-update ru"
$appMatches = [System.Management.Automation.CommandCompletion]::CompleteInput($appLine, $appLine.Length, $null)
if ('run' -notin $appMatches.CompletionMatches.CompletionText) { throw 'App-update subcommand Tab completion failed.' }
$bytes = [IO.File]::ReadAllBytes($exe)
$peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
if ([BitConverter]::ToUInt16($bytes, $peOffset + 24 + 68) -ne 2) { throw 'The EXE has a Console subsystem.' }
if ($bytes.Length -gt 100000000) { throw 'Portable EXE exceeds 100,000,000 bytes.' }
Write-Output "PASS: $ExpectedVersion CLI version/help/JSON/invalid arguments/completion/GUI subsystem/size. No Windows Setup invoked."
