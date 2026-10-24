param([string]$ArtifactDirectory = 'artifacts', [string]$PropsPath = 'Directory.Build.props',
    [Parameter(Mandatory)][string]$Commit)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AppUpdateRelease.psm1') -Force
$identity = Get-PcNinjaReleaseIdentity $PropsPath
if ($Commit -cnotmatch '\A[a-f0-9]{40}\z') { throw 'A full source commit is required.' }
$built = Join-Path $ArtifactDirectory $identity.BuildExecutableName
$versioned = Join-Path $ArtifactDirectory $identity.AssetName
if (Test-Path -LiteralPath $built) {
    if (Test-Path -LiteralPath $versioned) { throw 'Versioned asset already exists; do not overwrite it.' }
    Move-Item -LiteralPath $built -Destination $versioned -ErrorAction Stop
}
$exe = Get-Item -LiteralPath $versioned
if ($exe.PSIsContainer -or ($exe.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Final EXE must be a regular non-reparse file.' }
Assert-PcNinjaExecutableIdentity $identity $exe.VersionInfo.ProductName $exe.VersionInfo.FileVersion $exe.VersionInfo.ProductVersion $exe.Length $Commit
$hash = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$encoding = [Text.UTF8Encoding]::new($false)
$sums = @("$hash  $($identity.AssetName)")
$manifestPath = Join-Path $ArtifactDirectory $identity.ManifestName
if ($identity.HasManifest) {
    $noticePath = Join-Path 'docs/updates' "$($identity.Version).json"
    $notice = Get-Content -LiteralPath $noticePath -Raw | ConvertFrom-Json
    $manifest = New-PcNinjaAppUpdateManifest $identity $exe.Length $hash -Importance $notice.importance -Message $notice.message
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 4), $encoding)
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $sums += "$manifestHash  $($identity.ManifestName)"
} elseif (Test-Path -LiteralPath $manifestPath) { throw 'RC packaging must not contain a stable update manifest.' }
[IO.File]::WriteAllLines((Join-Path $ArtifactDirectory 'SHA256SUMS.txt'), $sums, [Text.Encoding]::ASCII)
[IO.File]::WriteAllLines((Join-Path $ArtifactDirectory 'BUILD-INFO.txt'),
    @("Version: $($identity.Version)", "Commit: $Commit", "EXE bytes: $($exe.Length)", "SHA-256: $hash"), $encoding)
Get-PcNinjaLocalAssetEvidence $identity $ArtifactDirectory
