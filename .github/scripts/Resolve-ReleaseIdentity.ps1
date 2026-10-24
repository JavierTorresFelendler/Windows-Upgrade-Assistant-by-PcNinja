param([string]$PropsPath = 'Directory.Build.props')
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AppUpdateRelease.psm1') -Force
$identity = Get-PcNinjaReleaseIdentity -PropsPath $PropsPath
if (-not (Test-Path -LiteralPath $identity.NotesPath -PathType Leaf)) { throw 'Version-specific release notes are missing.' }
if ($env:IS_TAG -eq 'true') {
    if ($env:REF_NAME -cne $identity.Tag) { throw 'Release tag and authoritative product version differ.' }
    if ($identity.IsBeta) {
        # Owner-authorized Beta tags can test unmerged PR code, never stable/latest.
        $runsText = gh api "repos/$($identity.Repository)/actions/runs?head_sha=$($env:GITHUB_SHA)&event=pull_request&per_page=20" | Out-String
        if ($LASTEXITCODE -ne 0) { throw 'Could not verify Beta PR validation.' }
        $runs = $runsText | ConvertFrom-Json
        $passed = @($runs.workflow_runs | Where-Object { $_.head_sha -ceq $env:GITHUB_SHA -and $_.name -ceq 'PR checks' -and $_.conclusion -ceq 'success' })
        if ($passed.Count -eq 0) { throw 'Beta publication requires passed PR checks for this exact source commit.' }
    } else {
        git merge-base --is-ancestor $env:GITHUB_SHA origin/main
        if ($LASTEXITCODE -ne 0) { throw 'Stable/RC releases require reviewed main ancestry.' }
    }
}
if ($env:GITHUB_ENV) {
    @("PRODUCT_VERSION=$($identity.Version)", "RELEASE_CHANNEL=$($identity.Channel)", "RELEASE_LABEL=$($identity.Label)",
        "ASSET_NAME=$($identity.AssetName)", "RELEASE_NOTES=$($identity.NotesPath)") | Add-Content -LiteralPath $env:GITHUB_ENV -Encoding utf8
}
$identity
