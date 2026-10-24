# Application update implementation contract

The owner replaced the earlier fixed Program Files/in-place replacement design
with consent-based side-by-side versioned portable EXEs. The authoritative
[user and acceptance guide](../APPLICATION-UPDATES.md) records behavior and limits.
Draft PR #13 remains the implementation/review history; RC8 is immutable.

- Core/AppUpdates/ApplicationUpdateClient.cs: pinned repository, isolated
  stable/Beta discovery, strict same-release manifest, bounded JSON/redirects/
  bytes/SHA-256. Normal/critical notice is bounded plain text; exact GitHub notes
  URL is allowlisted. Metadata discovery never fetches an EXE.
- AppUpdateFiles.cs: source-directory anchoring, private same-folder staging,
  exact product/PE/hash/signature validation, safe existing-file reuse, private
  copy/helper ACLs, no-overwrite publication and held launch guard.
- AppOperationGate.cs: secure non-thread-affine machine-wide exclusion, fresh
  Setup/restart checks and helper ownership transfer. Windows operations share it.
- AppUpdateInstaller.cs: internal protocol-v2 successor helper. Private fresh
  request binds exact parent process/start/path/hash and release. READY/GO,
  exact-parent exit wait and no-argument GUI relaunch; no legacy command replay.
  Despite the historical class name, this is not a software installer.
- MainForm.AppUpdates.cs: nonblocking check, explicit custom offer with Not now
  default, regular/critical badge/summary/version/GitHub notes, cancel/progress,
  preservation of independent Windows/media diagnostics.
- CliAppUpdates.cs: check/status/run, explicit elevated --yes, stable result codes
  and no GUI/browser/restart authorization. Handoff is not run completion.
- Directory.Build.props and .github/scripts: authoritative version, deterministic
  versioned asset name, validated notice configuration, draft-first immutable
  asset publication. Beta is prerelease/non-latest and may use a matching exact
  CI-passed PR commit; stable/RC still require main ancestry.

Manifest schema 1 fields: schemaVersion, product, repository, channel, version,
tag, assetName, assetUrl, sizeBytes, sha256, releaseNotesUrl, importance, message.
channel is stable or beta. Filenames and pinned URLs are derived strictly from
the validated release version, never broad wildcard/newest-file guesses.

Live Beta 1 -> Beta 2 testing remains distinct from synthetic/hosted checks.
Unsigned publisher trust, fixed local NTFS restriction and retained staging
are explicit limits. No main merge or stable publication follows automatically.
