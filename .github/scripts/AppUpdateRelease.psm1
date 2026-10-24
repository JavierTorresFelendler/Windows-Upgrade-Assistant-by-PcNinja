Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Repository = 'JavierTorresFelendler/Windows-Upgrade-Assistant-by-PcNinja'
$script:Product = 'Windows Upgrade Assistant By PcNinja'
$script:BuildExecutableName = 'Windows-Upgrade-Assistant-By-PcNinja.exe'
$script:ManifestName = 'update-manifest.json'
$script:MaximumBytes = 100000000L

function Get-PcNinjaVersionIdentity {
    param([Parameter(Mandatory)][string]$Version)
    $number = '(0|[1-9][0-9]*)'
    $match = [regex]::Match($Version, "\A$number\.$number\.$number(?:-(?:rc|beta)\.$number)?\z")
    if (-not $match.Success) { throw 'Release identity must be literal x.y.z or x.y.z-rc.N, without build metadata.' }
    $parts = $Version.Split('-')[0].Split('.')
    foreach ($part in $parts) {
        $value = 0
        if (-not [int]::TryParse($part, [ref]$value) -or $value -gt 65534) { throw 'Release version exceeds binary metadata limits.' }
    }
    $stable = -not $Version.Contains('-')
    $revision = if ($stable) { '0' } else { $Version.Split('.')[-1] }
    $revisionNumber = 0
    if (-not [int]::TryParse($revision, [ref]$revisionNumber) -or $revisionNumber -gt 65534 -or (-not $stable -and $revisionNumber -le 0)) { throw 'Prerelease revision exceeds binary metadata limits.' }
    $beta = $Version.Contains('-beta.')
    $label = if ($stable) { $Version } elseif ($beta) { "Beta$revision" } else { "RC$revision" }
    $display = "$($parts[0]).$($parts[1])" + $(if ($parts[2] -eq '0') { '' } else { ".$($parts[2])" })
    if (-not $stable) { $display += "-$label" }
    $asset = "Windows-Upgrade-Assistant-By-PcNinja-V$display.exe"
    [pscustomobject]@{
        Version = $Version; Tag = "v$Version"; IsStable = $stable; IsBeta = $beta; HasManifest = ($stable -or $beta)
        Channel = $(if ($stable) { 'stable' } elseif ($beta) { 'beta' } else { 'preview' })
        Label = $label; NotesPath = "docs/RELEASE-$label.md"
        FileVersion = "$($parts -join '.').$revision"
        Product = $script:Product; Repository = $script:Repository
        AssetName = $asset; BuildExecutableName = $script:BuildExecutableName; ManifestName = $script:ManifestName; MaximumBytes = $script:MaximumBytes
    }
}

function Get-PcNinjaReleaseIdentity {
    param([Parameter(Mandatory)][string]$PropsPath)
    [xml]$props = Get-Content -LiteralPath $PropsPath -Raw
    $nodes = @($props.SelectNodes('/Project/PropertyGroup/PcNinjaReleaseVersion'))
    if ($nodes.Count -ne 1) { throw 'Exactly one authoritative PcNinjaReleaseVersion is required.' }
    Get-PcNinjaVersionIdentity -Version ([string]$nodes[0].InnerText)
}

function New-PcNinjaAppUpdateManifest {
    param([Parameter(Mandatory)]$Identity, [Parameter(Mandatory)][long]$SizeBytes, [Parameter(Mandatory)][string]$Sha256,
        [ValidateSet('normal','critical')][string]$Importance = 'normal',
        [string]$Message = 'A new version of Windows Upgrade Assistant By PcNinja is available.')
    $checked = Get-PcNinjaVersionIdentity $Identity.Version
    if (-not $checked.HasManifest) { throw 'RC releases must not publish an application-update manifest.' }
    if ([string]::IsNullOrWhiteSpace($Message) -or $Message.Length -gt 1200 -or $Message -match '[\x00-\x09\x0B\x0C\x0E-\x1F\x7F-\x9F\u202A-\u202E\u2066-\u2069]') { throw 'Update message must be bounded plain text without control or direction overrides.' }
    if ($SizeBytes -le 0 -or $SizeBytes -gt $script:MaximumBytes -or $Sha256 -cnotmatch '\A[A-Fa-f0-9]{64}\z') {
        throw 'Stable manifest requires final bounded EXE bytes and a SHA-256 digest.'
    }
    [ordered]@{
        schemaVersion = 1; product = $script:Product; repository = $script:Repository; channel = $checked.Channel
        version = $checked.Version; tag = $checked.Tag; assetName = $checked.AssetName
        assetUrl = "https://github.com/$script:Repository/releases/download/$($checked.Tag)/$($checked.AssetName)"
        sizeBytes = $SizeBytes; sha256 = $Sha256.ToLowerInvariant()
        releaseNotesUrl = "https://github.com/$script:Repository/releases/tag/$($checked.Tag)"
        importance = $Importance; message = $Message
    }
}

function Assert-PcNinjaExecutableIdentity {
    param([Parameter(Mandatory)]$Identity, [Parameter(Mandatory)][string]$ProductName,
        [Parameter(Mandatory)][string]$FileVersion, [Parameter(Mandatory)][string]$ProductVersion,
        [Parameter(Mandatory)][long]$SizeBytes, [Parameter(Mandatory)][string]$Commit)
    $checked = Get-PcNinjaVersionIdentity $Identity.Version
    if ($Commit -cnotmatch '\A[a-f0-9]{40}\z' -or $ProductName -cne $checked.Product -or
        $FileVersion -cne $checked.FileVersion -or $ProductVersion -cne "$($checked.Version)+$Commit" -or
        $SizeBytes -le 0 -or $SizeBytes -gt $checked.MaximumBytes) {
        throw 'Final EXE product/version/source-commit identity or 100,000,000-byte cap failed verification.'
    }
}

function Get-PcNinjaLocalAssetEvidence {
    param([Parameter(Mandatory)]$Identity, [Parameter(Mandatory)][string]$ArtifactDirectory)
    $checked = Get-PcNinjaVersionIdentity $Identity.Version
    $directory = (Get-Item -LiteralPath $ArtifactDirectory).FullName
    $names = @($checked.AssetName, 'SHA256SUMS.txt', 'BUILD-INFO.txt')
    if ($checked.HasManifest) { $names += $script:ManifestName }
    elseif (Test-Path -LiteralPath (Join-Path $directory $script:ManifestName)) { throw 'An RC artifact directory contains a stable manifest.' }
    $evidence = [Collections.Generic.List[object]]::new()
    foreach ($name in $names) {
        $file = Get-Item -LiteralPath (Join-Path $directory $name)
        if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $file.Length -le 0) {
            throw 'Release assets must be nonempty regular files, never reparse points.'
        }
        if ($name -eq $checked.AssetName -and $file.Length -gt $script:MaximumBytes) { throw 'Portable EXE exceeds 100,000,000 bytes.' }
        if ($name -eq $script:ManifestName -and $file.Length -gt 131072) { throw 'Stable manifest exceeds its client JSON limit.' }
        $evidence.Add([pscustomobject]@{
            Name = $name; Path = $file.FullName; Size = [long]$file.Length
            Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            Url = "https://github.com/$script:Repository/releases/download/$($checked.Tag)/$name"
        })
    }
    if ($checked.HasManifest) {
        Assert-PcNinjaManifestDocument $checked (Get-Content -LiteralPath (Join-Path $directory $script:ManifestName) -Raw) $evidence[0].Size $evidence[0].Sha256
    }
    $evidence.ToArray()
}

function Assert-PcNinjaManifestDocument {
    param([Parameter(Mandatory)]$Identity, [Parameter(Mandatory)][string]$ManifestText,
        [Parameter(Mandatory)][long]$SizeBytes, [Parameter(Mandatory)][string]$Sha256)
    $options = [System.Text.Json.JsonDocumentOptions]::new()
    $options.MaxDepth = 16
    $document = [System.Text.Json.JsonDocument]::Parse($ManifestText, $options)
    try {
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw 'Manifest root must be an object.' }
        $importance = $document.RootElement.GetProperty('importance').GetString()
        $message = $document.RootElement.GetProperty('message').GetString()
        $expected = New-PcNinjaAppUpdateManifest $Identity $SizeBytes $Sha256 -Importance $importance -Message $message
        $properties = @($document.RootElement.EnumerateObject())
        if ($properties.Count -ne $expected.Count) { throw 'Stable manifest fields differ from the client schema.' }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $properties) {
            if (-not $seen.Add($property.Name) -or $property.Name -cnotin @($expected.Keys)) { throw 'Duplicate or unknown stable manifest field.' }
            $value = $expected[$property.Name]
            if ($value -is [string]) {
                if ($property.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String -or $property.Value.GetString() -cne $value) {
                    throw 'Stable manifest identity, URL or digest differs from the final asset.'
                }
            } elseif ($property.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or $property.Value.GetInt64() -ne [long]$value) {
                throw 'Stable manifest numeric value differs from the final asset.'
            }
        }
    } finally { $document.Dispose() }
}

function Assert-PcNinjaUploadedAssets {
    param([Parameter(Mandatory)]$Release, [Parameter(Mandatory)]$Identity,
        [Parameter(Mandatory)][object[]]$ExpectedAssets, [Parameter(Mandatory)][long]$ReleaseId,
        [Parameter(Mandatory)][bool]$MustBeDraft)
    if ([long]$Release.id -ne $ReleaseId -or $ReleaseId -le 0 -or $Release.tag_name -cne $Identity.Tag -or
        $Release.draft -isnot [bool] -or $Release.draft -ne $MustBeDraft -or
        $Release.prerelease -isnot [bool] -or $Release.prerelease -ne (-not $Identity.IsStable)) {
        throw 'GitHub release identity/channel/draft state changed.'
    }
    $assets = @($Release.assets)
    $wanted = if ($Identity.HasManifest) { 4 } else { 3 }
    if ($assets.Count -ne $wanted -or $ExpectedAssets.Count -ne $wanted) { throw 'Release asset count differs from its channel contract.' }
    $ids = [Collections.Generic.HashSet[long]]::new()
    foreach ($expected in $ExpectedAssets) {
        $found = @($assets | Where-Object { $_.name -ceq $expected.Name })
        if ($found.Count -ne 1) { throw 'A release asset is missing or duplicated.' }
        $asset = $found[0]
        # Draft assets can have GitHub's temporary untagged URL. They are bound
        # here by this exact release ID, unique asset IDs, names, sizes and hashes;
        # after publication ONLY the final pinned tag URL is accepted.
        $temporaryUrl = '\Ahttps://github\.com/' + [regex]::Escape($Identity.Repository) +
            '/releases/download/untagged-[a-f0-9]{16,64}/' + [regex]::Escape($expected.Name) + '\z'
        $urlValid = $asset.browser_download_url -ceq $expected.Url -or
            ($MustBeDraft -and $asset.browser_download_url -cmatch $temporaryUrl)
        if ([long]$asset.id -le 0 -or -not $ids.Add([long]$asset.id) -or $asset.state -cne 'uploaded' -or
            [long]$asset.size -ne $expected.Size -or -not $urlValid -or
            $asset.digest -inotmatch '\Asha256:[a-f0-9]{64}\z' -or $asset.digest -ine "sha256:$($expected.Sha256)") {
            throw 'Uploaded asset ID, byte count, pinned URL or SHA-256 digest failed verification.'
        }
    }
}

function Assert-PcNinjaStableAdvance {
    param([Parameter(Mandatory)]$Identity, $Latest)
    if (-not $Identity.IsStable -or $null -eq $Latest) { return }
    if ($Latest.draft -ne $false -or $Latest.prerelease -ne $false -or
        -not ([string]$Latest.tag_name).StartsWith('v', [StringComparison]::Ordinal)) { throw 'Latest stable metadata is invalid.' }
    $previous = Get-PcNinjaVersionIdentity -Version (([string]$Latest.tag_name).Substring(1))
    if (-not $previous.IsStable -or [version]$Identity.Version -le [version]$previous.Version) {
        throw 'A stable release must advance the existing stable version, never move latest backwards.'
    }
}

function Publish-PcNinjaVerifiedRelease {
    param([Parameter(Mandatory)]$Identity, [Parameter(Mandatory)][string]$ArtifactDirectory,
        [Parameter(Mandatory)][string]$Notes, [Parameter(Mandatory)][string]$Commit,
        [Parameter(Mandatory)][scriptblock]$GitHub)
    $checked = Get-PcNinjaVersionIdentity $Identity.Version
    if ($Commit -cnotmatch '\A[a-f0-9]{40}\z') { throw 'A full reviewed release commit is required.' }
    $evidence = @(Get-PcNinjaLocalAssetEvidence $checked $ArtifactDirectory)
    $existing = & $GitHub 'GetRelease' @{ Tag = $checked.Tag }
    if ($null -ne $existing) {
        if ($existing.draft -ne $true -or @($existing.assets).Count -ne 0 -or
            $existing.tag_name -cne $checked.Tag -or $existing.target_commitish -cne $Commit -or
            $existing.prerelease -ne (-not $checked.IsStable)) { throw 'Release already exists; no published release or existing asset may be replaced.' }
    }
    if ($checked.IsStable) { Assert-PcNinjaStableAdvance $checked (& $GitHub 'GetLatest' @{}) }
    $draft = if ($null -ne $existing) { $existing } else {
        & $GitHub 'CreateDraft' @{ Tag = $checked.Tag; Commit = $Commit; Notes = $Notes
            Name = "$script:Product $($checked.Label)"; Prerelease = (-not $checked.IsStable) }
    }
    if ($null -eq $draft -or [long]$draft.id -le 0 -or $draft.draft -ne $true -or $draft.tag_name -cne $checked.Tag -or
        $draft.prerelease -ne (-not $checked.IsStable) -or @($draft.assets).Count -ne 0) { throw 'GitHub did not return an empty matching draft.' }
    $releaseId = [long]$draft.id
    & $GitHub 'UploadAssets' @{ Tag = $checked.Tag; Paths = @($evidence | ForEach-Object Path) } | Out-Null
    $uploaded = & $GitHub 'GetRelease' @{ Tag = $checked.Tag; Id = $releaseId }
    Assert-PcNinjaUploadedAssets $uploaded $checked $evidence $releaseId $true
    $again = @(Get-PcNinjaLocalAssetEvidence $checked $ArtifactDirectory)
    for ($index = 0; $index -lt $evidence.Count; $index++) {
        if ($again[$index].Name -cne $evidence[$index].Name -or $again[$index].Size -ne $evidence[$index].Size -or
            $again[$index].Sha256 -cne $evidence[$index].Sha256) { throw 'Local release assets changed during upload.' }
    }
    if ($checked.IsStable) { Assert-PcNinjaStableAdvance $checked (& $GitHub 'GetLatest' @{}) }
    & $GitHub 'Publish' @{ Id = $releaseId; Prerelease = (-not $checked.IsStable); Latest = $checked.IsStable } | Out-Null
    $published = & $GitHub 'GetRelease' @{ Tag = $checked.Tag; Id = $releaseId }
    Assert-PcNinjaUploadedAssets $published $checked $evidence $releaseId $false
    if ($checked.IsStable) {
        $latest = & $GitHub 'GetLatest' @{}
        if ($null -eq $latest -or [long]$latest.id -ne $releaseId -or $latest.draft -ne $false -or $latest.prerelease -ne $false) {
            throw 'Published stable release was not confirmed as latest.'
        }
    }
    [pscustomobject]@{ Tag = $checked.Tag; ReleaseId = $releaseId; Channel = $checked.Channel; AssetsVerified = $evidence.Count }
}

Export-ModuleMember -Function Get-PcNinjaVersionIdentity, Get-PcNinjaReleaseIdentity, New-PcNinjaAppUpdateManifest,
    Assert-PcNinjaExecutableIdentity, Get-PcNinjaLocalAssetEvidence, Assert-PcNinjaManifestDocument, Assert-PcNinjaUploadedAssets,
    Assert-PcNinjaStableAdvance, Publish-PcNinjaVerifiedRelease
