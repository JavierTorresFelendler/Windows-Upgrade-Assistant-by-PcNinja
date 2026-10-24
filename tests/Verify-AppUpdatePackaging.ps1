# Offline fixtures: mocked publication and ordinary tiny bytes, never network/app/Setup execution.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repositoryRoot '.github/scripts/AppUpdateRelease.psm1') -Force
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('PcNinja-AppUpdatePackaging-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)
$fixtureCommit = 'a' * 40

function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function MustReject([scriptblock]$Action, [string]$Message) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Require $rejected $Message
}
function New-FixtureArtifacts($Identity) {
    $directory = Join-Path $fixtureRoot ([guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    foreach ($name in @($Identity.AssetName, 'SHA256SUMS.txt', 'BUILD-INFO.txt')) {
        [IO.File]::WriteAllText((Join-Path $directory $name), "ordinary harmless fixture bytes for $name", $encoding)
    }
    if ($Identity.HasManifest) {
        $exe = Get-Item -LiteralPath (Join-Path $directory $Identity.AssetName)
        $hash = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash
        $manifest = New-PcNinjaAppUpdateManifest $Identity $exe.Length $hash
        [IO.File]::WriteAllText((Join-Path $directory $Identity.ManifestName), ($manifest | ConvertTo-Json -Depth 4), $encoding)
    }
    $directory
}
function New-FixtureRelease($Identity, $Evidence, [bool]$Draft, [string]$Commit) {
    $assetId = 1000L
    [pscustomobject]@{
        id = 701L; tag_name = $Identity.Tag; target_commitish = $Commit; draft = $Draft; prerelease = (-not $Identity.IsStable)
        assets = @($Evidence | ForEach-Object {
            $assetId++
            [pscustomobject]@{ id = $assetId; name = $_.Name; state = 'uploaded'; size = $_.Size
                digest = "sha256:$($_.Sha256)"; browser_download_url = $_.Url }
        })
    }
}
function Invoke-PublicationFixture($Identity, [string]$Mode = 'Good') {
    $directory = New-FixtureArtifacts $Identity
    $evidence = @(Get-PcNinjaLocalAssetEvidence $Identity $directory)
    $state = [pscustomobject]@{ Calls = [Collections.Generic.List[string]]::new(); Release = $null; PublishCount = 0; LatestCalls = 0 }
    if ($Mode -eq 'ExistingPublished') { $state.Release = New-FixtureRelease $Identity $evidence $false $fixtureCommit }
    if ($Mode -eq 'ExistingDraftAssets') { $state.Release = New-FixtureRelease $Identity $evidence $true $fixtureCommit }
    $makeRelease = ${function:New-FixtureRelease}
    $assert = ${function:Require}
    $commit = $fixtureCommit
    $adapter = {
        param($operation, $parameters)
        $state.Calls.Add($operation)
        switch ($operation) {
            'GetRelease' {
                if ($Mode -eq 'DraftHiddenByTag' -and $null -ne $state.Release -and $state.Release.draft -and -not $parameters.ContainsKey('Id')) { return $null }
                if ($parameters.ContainsKey('Id')) { & $assert ($parameters.Id -eq 701L) 'Post-create lookup lost its immutable release ID.' }
                return $state.Release
            }
            'GetLatest' {
                $state.LatestCalls++
                if ($state.PublishCount -gt 0) { return $state.Release }
                if ($Mode -eq 'LatestNewer' -or ($Mode -eq 'RacingLatest' -and $state.LatestCalls -gt 1)) {
                    return [pscustomobject]@{ id = 900L; tag_name = 'v9.9.9'; draft = $false; prerelease = $false }
                }
                return $null
            }
            'CreateDraft' {
                & $assert ($parameters.Prerelease -eq (-not $Identity.IsStable)) 'Draft channel changed.'
                $state.Release = [pscustomobject]@{ id = 701L; tag_name = $parameters.Tag; target_commitish = $parameters.Commit
                    draft = $true; prerelease = $parameters.Prerelease; assets = @() }
                return $state.Release
            }
            'UploadAssets' {
                if ($Mode -eq 'UploadFailure') { throw 'Synthetic upload failure.' }
                & $assert ($parameters.Paths.Count -eq $evidence.Count) 'Incorrect upload asset count.'
                $state.Release = & $makeRelease $Identity $evidence $true $commit
                switch ($Mode) {
                    'BadDigest' { $state.Release.assets[0].digest = 'sha256:' + ('0' * 64) }
                    'MissingDigest' { $state.Release.assets[0].digest = $null }
                    'BadSize' { $state.Release.assets[0].size++ }
                    'DuplicateId' { $state.Release.assets[1].id = $state.Release.assets[0].id }
                    'WrongUrl' { $state.Release.assets[0].browser_download_url = 'https://evil.test/file' }
                    { $_ -in @('DraftTemporaryUrls', 'PublishedTemporaryUrls') } {
                        foreach ($asset in $state.Release.assets) {
                            $asset.browser_download_url = $asset.browser_download_url.Replace("/$($Identity.Tag)/", '/untagged-abcdef0123456789abcd/')
                        }
                    }
                    'MissingAsset' { $state.Release.assets = @($state.Release.assets | Select-Object -Skip 1) }
                    'ExtraAsset' { $state.Release.assets += $state.Release.assets[0] }
                    'WrongDraft' { $state.Release.draft = $false }
                    'LocalChanged' { [IO.File]::WriteAllText((Join-Path $directory 'BUILD-INFO.txt'), 'changed fixture after upload', $encoding) }
                }
            }
            'Publish' {
                & $assert ($state.Release.draft -eq $true -and $parameters.Id -eq 701) 'Publication preceded a verified draft.'
                & $assert ($parameters.Latest -eq $Identity.IsStable -and $parameters.Prerelease -eq (-not $Identity.IsStable)) 'Publication changed channel/latest behavior.'
                $state.PublishCount++
                $state.Release.draft = $false
                if ($Mode -eq 'DraftTemporaryUrls') {
                    foreach ($asset in $state.Release.assets) {
                        $asset.browser_download_url = $asset.browser_download_url.Replace('/untagged-abcdef0123456789abcd/', "/$($Identity.Tag)/")
                    }
                }
            }
            default { throw 'Unexpected synthetic GitHub operation.' }
        }
    }.GetNewClosure()
    $failure = $null
    $result = $null
    try { $result = Publish-PcNinjaVerifiedRelease $Identity $directory 'Synthetic release notes' $fixtureCommit $adapter }
    catch { $failure = $_ }
    [pscustomobject]@{ State = $state; Result = $result; Failure = $failure }
}

try {
    $rc = Get-PcNinjaVersionIdentity '1.1.0-rc.8'
    $stable = Get-PcNinjaVersionIdentity '1.1.0'
    $beta = Get-PcNinjaVersionIdentity '1.1.0-beta.1'
    Require ($beta.Channel -ceq 'beta' -and $beta.AssetName -ceq 'Windows-Upgrade-Assistant-By-PcNinja-V1.1-Beta1.exe') 'Beta filename/channel mismatch.'
    Require (-not $rc.IsStable -and $rc.NotesPath -ceq 'docs/RELEASE-RC8.md' -and $rc.FileVersion -ceq '1.1.0.8') 'RC identity/notes contract changed.'
    Require ($stable.IsStable -and $stable.NotesPath -ceq 'docs/RELEASE-1.1.0.md' -and $stable.FileVersion -ceq '1.1.0.0') 'Stable identity/notes contract changed.'
    foreach ($identity in @($rc, $stable, $beta)) {
        Assert-PcNinjaExecutableIdentity $identity $identity.Product $identity.FileVersion "$($identity.Version)+$fixtureCommit" 42 $fixtureCommit
        MustReject { Assert-PcNinjaExecutableIdentity $identity $identity.Product $identity.FileVersion "$($identity.Version)+$('b' * 40)" 42 $fixtureCommit } 'Stale same-version EXE source commit was accepted.'
        MustReject { Assert-PcNinjaExecutableIdentity $identity $identity.Product $identity.FileVersion $identity.Version 42 $fixtureCommit } 'Missing EXE source-commit metadata was accepted.'
        MustReject { Assert-PcNinjaExecutableIdentity $identity $identity.Product '9.9.9.0' "$($identity.Version)+$fixtureCommit" 42 $fixtureCommit } 'Wrong EXE file version was accepted.'
        MustReject { Assert-PcNinjaExecutableIdentity $identity 'Other product' $identity.FileVersion "$($identity.Version)+$fixtureCommit" 42 $fixtureCommit } 'Wrong EXE product was accepted.'
        MustReject { Assert-PcNinjaExecutableIdentity $identity $identity.Product $identity.FileVersion "$($identity.Version)+$fixtureCommit" 100000001 $fixtureCommit } 'Oversize actual EXE metadata was accepted.'
    }
    foreach ($invalid in @('v1.1.0', '1.1', '1.1.0.0', '01.1.0', '1.1.0-beta.01', '1.1.0-beta.0', '1.1.0+metadata', '1.1.0-rc.08', "1.1.0`n", '70000.1.0')) {
        MustReject { Get-PcNinjaVersionIdentity $invalid } 'Malformed release identity was accepted.'
    }
    $props = Join-Path $fixtureRoot 'Directory.Build.props'
    [IO.File]::WriteAllText($props, '<Project><PropertyGroup><PcNinjaReleaseVersion>1.1.0</PcNinjaReleaseVersion></PropertyGroup></Project>', $encoding)
    Require ((Get-PcNinjaReleaseIdentity $props).Version -ceq '1.1.0') 'Authoritative props version was not read.'
    [IO.File]::WriteAllText($props, '<Project><PropertyGroup><PcNinjaReleaseVersion>$(OtherVersion)</PcNinjaReleaseVersion></PropertyGroup></Project>', $encoding)
    MustReject { Get-PcNinjaReleaseIdentity $props } 'An evaluated/nonliteral source version was accepted.'
    [IO.File]::WriteAllText($props, '<Project><PropertyGroup><PcNinjaReleaseVersion>1.1.0</PcNinjaReleaseVersion><PcNinjaReleaseVersion>1.1.1</PcNinjaReleaseVersion></PropertyGroup></Project>', $encoding)
    MustReject { Get-PcNinjaReleaseIdentity $props } 'Duplicate release identity was accepted.'
    $hash = 'A' * 64
    $manifest = New-PcNinjaAppUpdateManifest $stable 42 $hash
    $json = $manifest | ConvertTo-Json -Depth 4
    $keys = @('schemaVersion', 'product', 'repository', 'channel', 'version', 'tag', 'assetName', 'assetUrl', 'sizeBytes', 'sha256', 'releaseNotesUrl', 'importance', 'message')
    Require ($manifest.Count -eq 13 -and @($manifest.Keys | Where-Object { $_ -cnotin $keys }).Count -eq 0) 'Manifest fields differ from AppUpdateManifest schema.'
    Require ($manifest.assetUrl -ceq "https://github.com/$($stable.Repository)/releases/download/v1.1.0/$($stable.AssetName)" -and
        $manifest.releaseNotesUrl -ceq "https://github.com/$($stable.Repository)/releases/tag/v1.1.0" -and $manifest.sha256 -ceq $hash.ToLowerInvariant()) 'Manifest URLs/hash are not pinned/canonical.'
    Assert-PcNinjaManifestDocument $stable $json 42 $hash
    $critical = New-PcNinjaAppUpdateManifest $beta 42 $hash -Importance critical -Message 'Security correction: please download the new version.'
    Assert-PcNinjaManifestDocument $beta ($critical | ConvertTo-Json) 42 $hash
    MustReject { New-PcNinjaAppUpdateManifest $beta 42 $hash -Importance urgent } 'Unknown importance accepted.'
    MustReject { New-PcNinjaAppUpdateManifest $beta 42 $hash -Message ('x' * 1201) } 'Unbounded publisher message accepted.'
    MustReject { New-PcNinjaAppUpdateManifest $rc 42 $hash } 'RC created a stable manifest.'
    MustReject { New-PcNinjaAppUpdateManifest $stable 100000001 $hash } 'Oversize EXE entered manifest.'
    MustReject { New-PcNinjaAppUpdateManifest $stable 0 $hash } 'Empty EXE entered manifest.'
    MustReject { New-PcNinjaAppUpdateManifest $stable 42 ('G' * 64) } 'Invalid hash entered manifest.'
    foreach ($invalidJson in @($json.Replace('"schemaVersion": 1', '"schemaVersion": "1"'),
        $json.Replace('"product":', '"Product":'), $json.Replace('"stable"', '"preview"'),
        $json.Replace('"schemaVersion": 1', '"schemaVersion": 1, "schemaVersion": 1'),
        $json.Replace('releases/download/v1.1.0', 'releases/latest/download'), $json.Replace('"sizeBytes": 42', '"sizeBytes": 43'))) {
        MustReject { Assert-PcNinjaManifestDocument $stable $invalidJson 42 $hash } 'Malformed/unbound manifest document was accepted.'
    }
    foreach ($identity in @($rc, $stable, $beta)) {
        $good = Invoke-PublicationFixture $identity
        if ($null -ne $good.Failure) { throw $good.Failure }
        Require ($good.State.PublishCount -eq 1 -and $good.Result.AssetsVerified -eq $(if ($identity.HasManifest) { 4 } else { 3 })) 'Publication asset count/channel changed.'
        $expectedCalls = if ($identity.IsStable) { 'GetRelease,GetLatest,CreateDraft,UploadAssets,GetRelease,GetLatest,Publish,GetRelease,GetLatest' }
            else { 'GetRelease,CreateDraft,UploadAssets,GetRelease,Publish,GetRelease' }
        Require (($good.State.Calls -join ',') -ceq $expectedCalls) 'Publication was not draft/upload/verify/publish-last.'
        $hiddenDraft = Invoke-PublicationFixture $identity 'DraftHiddenByTag'
        if ($null -ne $hiddenDraft.Failure) { throw $hiddenDraft.Failure }
        Require ($hiddenDraft.State.PublishCount -eq 1) 'A hidden-by-tag draft was not verified by ID.'
        $temporaryDraft = Invoke-PublicationFixture $identity 'DraftTemporaryUrls'
        if ($null -ne $temporaryDraft.Failure) { throw $temporaryDraft.Failure }
        Require ($temporaryDraft.State.PublishCount -eq 1) 'Temporary draft URLs were not resolved to strict published URLs.'
        $temporaryPublished = Invoke-PublicationFixture $identity 'PublishedTemporaryUrls'
        Require ($null -ne $temporaryPublished.Failure -and $temporaryPublished.State.PublishCount -eq 1) 'A temporary URL was accepted after publication.'
        foreach ($mode in @('BadDigest', 'MissingDigest', 'BadSize', 'DuplicateId', 'WrongUrl', 'MissingAsset', 'ExtraAsset', 'WrongDraft',
            'UploadFailure', 'ExistingPublished', 'ExistingDraftAssets', 'LocalChanged')) {
            $bad = Invoke-PublicationFixture $identity $mode
            Require ($null -ne $bad.Failure -and $bad.State.PublishCount -eq 0) "Unsafe publication was not stopped before publish: $mode"
        }
    }
    foreach ($mode in @('LatestNewer', 'RacingLatest')) {
        $bad = Invoke-PublicationFixture $stable $mode
        Require ($null -ne $bad.Failure -and $bad.State.PublishCount -eq 0) 'Stable latest could move backwards.'
    }
    $rcDirectory = New-FixtureArtifacts $rc
    [IO.File]::WriteAllText((Join-Path $rcDirectory $rc.ManifestName), '{}', $encoding)
    MustReject { Get-PcNinjaLocalAssetEvidence $rc $rcDirectory } 'RC silently included a stable manifest.'
    foreach ($script in Get-ChildItem -LiteralPath (Join-Path $repositoryRoot '.github/scripts') -Filter '*.ps1') {
        $parseErrors = $null
        [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$null, [ref]$parseErrors) | Out-Null
        Require ($parseErrors.Count -eq 0) "Workflow helper parse error: $($script.Name)"
    }
    Write-Output 'PASS: Offline RC/stable props/manifest identity, exact final-byte binding, immutable assets, draft-first digest verification, latest-last publication and fail-closed lifecycle fixtures. No network, app/ISO/Setup execution or GitHub publication.'
} finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolved)).StartsWith('PcNinja-AppUpdatePackaging-', [StringComparison]::Ordinal)) {
        throw 'Fixture cleanup target escaped its specifically owned temporary directory.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
