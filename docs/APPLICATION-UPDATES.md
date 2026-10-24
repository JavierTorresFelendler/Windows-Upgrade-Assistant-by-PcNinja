# Portable application updates — Beta trial

This feature is developed in Draft PR #13. Published RC8 and all older assets
stay unchanged. The owner approved a live Beta 1 -> Beta 2 demonstration before
RC9 or a stable V1.1. Beta 2 waits until the owner has opened/tested Beta 1.
No main merge, RC9/stable publication or developer-machine Windows upgrade is
authorized by the Beta trial.

## User flow

1. Check this project's GitHub release metadata at startup or via Check app updates.
   This check downloads metadata only, never an EXE.
2. A newer version opens an offer with target version, **REGULAR UPDATE** or
   **CRITICAL UPDATE**, a short publisher message and **View release notes on GitHub**.
3. **Not now**, Escape or closing the offer keeps the current tool. Opening notes
   opens the exact GitHub release page, not a download and not update consent.
4. Only **Download and run the new version** starts transfer/verification.
5. Save the version-named EXE beside the running tool, not the shell's working
   directory. A private hidden `.pcninja-updates/<operation-id>` staging directory
   is below that same folder. There is no Program Files destination or fallback.
6. Verify bytes/hash/size/product/version/publisher continuity. A verified helper
   waits for the exact old process to exit, then GUI opens the new EXE with NO
   arguments. The previous EXE stays unchanged as the usable old version.

No installer, original replacement, old-file rename, numbered Copy N, service,
scheduled task, Windows Setup or forced restart is involved. Cancellation/failure
before handoff keeps the old window open. A helper/launch failure does not remove
the old EXE; open it again. A correctly verified existing exact-version file is
reused; a mismatch is not overwritten/deleted and causes an actionable refusal.
Readonly/unsupported locations fail, never relocate silently.

## Version names and channels

Binary/product identity is authoritative, not a guessed filename. Internal build
output remains Windows-Upgrade-Assistant-By-PcNinja.exe; published assets are:

| Product version | Published filename | Update discovery |
| --- | --- | --- |
| 1.1.0-beta.1 | Windows-Upgrade-Assistant-By-PcNinja-V1.1-Beta1.exe | Beta only |
| 1.1.0-beta.2 | Windows-Upgrade-Assistant-By-PcNinja-V1.1-Beta2.exe | Beta only |
| 1.1.0 | Windows-Upgrade-Assistant-By-PcNinja-V1.1.exe | Stable only |
| 1.1.1 | Windows-Upgrade-Assistant-By-PcNinja-V1.1.1.exe | Stable only |

Beta discovery checks up to three pages of five recent release records, bounded
by metadata/time limits, and selects the highest numbered published Beta. It
does not accept drafts, RCs or stable releases. Stable/RC binaries use only latest
stable; they never request the Beta list. Missing Beta/stable releases are normal
results. An offline/rate-limited/invalid check does not block normal tool use.

## Publisher-controlled regular/critical notices

Each published stable/Beta manifest includes `importance` (`normal` or `critical`)
and `message` (1-1200 plain-text characters). Source configuration is
`docs/updates/{version}.json`. The release workflow validates the fields and binds
them, version, exact EXE URL, size and SHA-256 to that release's manifest.

Critical gets a prominent colored badge and recommendation, not automatic or
mandatory download. The default button remains Not now. Do not invent a security
claim: explain the actual fix accurately; label critical-style trial notices as
demonstrations if used in Beta. Full details belong in that version's release
notes. The GUI renders plain text, not HTML/scripts.

Published manifests/assets are immutable. To change the notice or binary, publish
a new version; never replace an old asset. Stable/latest is never advanced by Beta.

## Safety, status and trust

Only ordinary fixed local NTFS paths are currently accepted. Copy off network,
ISO/removable/reparse/unsupported folders first. Administrator elevation does
not make readonly media writable. Staging directories are created atomically
with Administrators/SYSTEM ACLs; unsafe existing staging is refused, not taken over.
Reused files are copied into private staging and their ACLs revalidated/hardened;
CopyFile's source permissions must not leak into the privileged helper.
Exact candidate/ancestor read/no-write/no-delete handles remain held through
launch. Existing files are never overwritten. Private handoff files are retained
for diagnosis; do not broadly clean the staging root or replay helper requests.

The machine-wide gate excludes app updating from Core ISO preparation/Setup
across instances; fresh external Setup/pending-restart checks also apply. The
updater never force-closes Windows work, replays upgrade arguments or reboots.
SHA-256 and fixed GitHub URLs bind bytes to the manifest, not an independent
publisher identity. EXEs are currently unsigned; a compromised publishing account
could change both manifest and binary. A signed current app requires the same
trusted signing certificate in its successor. Consent/critical styling does not
replace code signing or prevent all supply-chain compromise.

CLI: `app-update check --json`, `app-update status --json`,
`app-update run --yes --json`. The unreleased install verb is an alias, not an
installer. run needs explicit elevated --yes; CLI never opens a GUI, browser or
hidden prompt. APP_UPDATE_HANDOFF_STARTED is not proof the new process ran.
After helper completion, correlate `operationId` in local
`.pcninja-updates/Last-AppUpdate.json` and run the new EXE's --version. Status is
bounded/read-only and may be null/unavailable without folder access.
Every updater result has setupStarted=false and readyForRestart=false.

## Live Beta acceptance

Use a disposable Windows 10/11 VM/snapshot. Manually open Beta 1 first; no Beta 2
is published yet. After owner confirmation, publish a separately verified Beta 2
with increased binary/tag/manifest version. Test decline/notes with no EXE
transfer, explicit download, exact same-folder files, new version relaunch with
no arguments, original preserved, no repeated offer from Beta 2, existing-file
reuse, cancellation/offline/mismatch and regular/critical notices. No Windows
upgrade is required for this experiment. CI/fake callbacks do not prove the live
helper/elevation/relaunch sequence or signed-binary acceptance.
