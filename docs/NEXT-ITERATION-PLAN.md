# Proposed next iteration and PR sequence

Status: staged execution authorized by the owner on 2026-10-17. PRs #8, #10 and
#11 merged; the narrowed manual Windows 10 POC is in development. Stop for owner VM
tests when needed. Stable publication and Internet self-update remain a separate
owner checkpoint; no published RC5 asset replacement is authorized.

## Latest owner evidence

The owner reports completing a further round of upgrades from Windows 10 through
Windows 11 26H2 successfully and is satisfied with the last test executable.
The exact artifact/commit and per-scenario source types were not restated; confirm
the tested checkpoint before treating this as acceptance of a particular PR.
Do not erase the earlier Appraiser crash or mount/eject findings: successful
later upgrades do not establish their original root causes or resolve every case.

## Approved staged delivery sequence

| Order | PR/work item | Acceptance boundary |
| --- | --- | --- |
| 1 | PR #8 merged as the next source baseline | Completed; squash d2f671c; published RC5 unchanged |
| 2 | Automatic-flow and diagnostics PR #10 | Merged after CI and owner feedback; add a separate small manual-restart explanation follow-up without changing Setup launch |
| 3 | A separate target-aware Windows 10 / same-version POC PR | In development: by explicit owner request, recognize Windows 10 and switch command without intra-Windows-10 build-order checks; let Setup decide migration; manual media and VM tests only, no clean fallback |
| Deferred | Design PR #9 safe-media preparation | Owner explicitly postponed this for the future in-ISO EXE; keep Draft, no runtime change or merge in the current pass |
| 5 | Application self-update design | Proposed in design/APPLICATION-UPDATES.md; no runtime/feed/release change. Implementation and live version-pair test require their own owner checkpoint |

Do not wait for every future feature to merge a useful tested improvement. Each
runtime PR gets CI and a commit-identified test EXE; changed upgrade flows need
owner VM tests before promotion. A small presentation-only follow-up can be
checked with local client GUI tests without rerunning Setup on the developer.
Release tags/RC publication remain separate explicit decisions. PR #9 currently
contains design only, so its green CI does not test the proposed runtime feature.
If validation caching or mount cleanup needs a larger safety redesign, split it
from the small confirmation/diagnostics PR rather than hiding untested behavior
inside a usability patch. Self-update design may proceed while field tests run,
but must not change the executable currently being evaluated.

## Automatic mode: one upfront assistant confirmation

- Capture an immutable operation intent at Start, before any download. Show that
  consent covers downloading, validating and starting the compatible in-place
  upgrade, plus the selected automatic/manual restart policy. Default Cancel.
- After successful download/validation and a unique compatible image selection,
  continue without a second assistant Start question. Preserve cancellation until
  actual Setup launch. Do not add `/quiet` merely to hide this assistant's dialog.
- Manual ISO/folder/mounted-media mode keeps its current post-validation review
  and final confirmation. No global confirmation bypass and no third source mode.
- The current GUI switches `_existingMedia.Checked` after a download: that display
  state must not erase the original automatic intent or grant automatic intent to
  a later manual source. A boolean tied only to the current radio is insufficient.
- Browser fallback, changed source/target/restart policy, ambiguous or incompatible
  indexes, unsafe media, insufficient space or failure cancels unattended
  continuation. Require the relevant corrective action/new consent, not a guessed
  image, warning bypass, clean install or forced reboot.
- Unsafe-ISO detach consent remains scoped to its device list; upfront upgrade
  consent does not permit arbitrary dismounts. Windows Setup may still need user
  intervention for errors; this proposal removes redundant assistant prompts,
  not every possible OS-owned dialog.
- Tests: consent before transfer, Cancel causes no transfer/Setup, unique valid
  automatic image starts once with no second prompt, manual path still confirms,
  fallback/change invalidates consent, no double-click launches, safety still blocks.

## Windows 10 targets and same-version repair

The owner superseded the original strict-version design with a simpler POC:
reuse the current manual image selector, identify Windows 10 from image metadata,
switch command and do not compare older/equal/newer Windows 10 builds. See
[the current scope/test checklist](WINDOWS10-POC.md). There is no 22H1 release.
Windows 10 Home/Pro normal support ended in October 2025; ESU eligibility is
separate. Offer this as an explicit advanced upgrade/repair choice with lifecycle
information, not as the default supported destination for every user.

The target media determines the Setup profile, not just the installed OS. Both
Windows 10 and 11 report NT version 10.0; major-version strings alone cannot
select the profile. Use client image metadata for the family; do not infer a
precise Windows 10 feature release from filenames or shared-base file versions.
No registry extraction or exact feature-release resolver is part of this POC.

Windows 10 command profile (never run it on the developer):

```cmd
setup.exe /auto upgrade /dynamicupdate disable /compat ignorewarning
```

Do not add `/product server` for Windows 10 targets. `/EULA accept` is documented
as new with Windows 11 and should not be copied blindly into older Setup commands.
Bind `/imageindex` and `/installfrom` to the actual validated file/index as today;
append `/noreboot` only for the user's manual-restart policy. Preserve the existing
experimental Windows 11 profile unchanged. Core eligibility and consumers must
change together; removing the old Windows 11-only gate alone is insufficient.

Accept older/equal/newer Windows 10 images for a Windows 10 host in synthetic
tests, then test actual migration/repair/refusal in disposable VM snapshots.
Preserve Windows 11 -> 10 blocking, Windows 11 target build comparisons,
edition/language/x64/client and all safety gates. Setup decides whether apps/data
can be kept; never fall back to clean or DataOnly. Record Windows 10 outcome
manually rather than auto-confirming a shared-base metadata build. This does not
promise that an older-image attempt can perform an in-place downgrade.

Update eligibility, command building, history/reporting, GUI wording, CLI/help and
fixtures together in Core-driven operations. Automatic download remains the
configured Windows 11 en-US source; adding a Windows 10 catalog is separate work.

## Application self-update (separate from OS upgrade)

The owner requested planning, not activation while PR #12 is under test. See
[the scoped design and two-stable-release acceptance plan](design/APPLICATION-UPDATES.md).
The first official version must already include the future updater; only then
can it be tested against a higher official version. Version numbers in the design
are examples, not approved tags. No live manifest or release workflow is changed.

- Reuse the existing PcNinja release-manifest approach, adapted for this .NET
  portable product; do not copy MSI replacement behavior into a portable EXE.
- Check asynchronously, allow failure/offline use, compare complete release
  versions including prerelease ordering, and download only a newer explicitly
  selected release asset over HTTPS. Verify SHA-256 and signing policy honestly;
  a checksum does not turn an unsigned EXE into a signed one.
- Keep stable and opt-in preview separate. GitHub's latest-release endpoint
  excludes prereleases; RC detection needs explicit preview policy. Temporary
  authenticated/expiring PR artifacts are not a customer update feed.
- Start with an explicit user-approved verified portable handoff, keep the old
  file, and block app replacement during active copy/download/preparation/Setup.
  Never expose a developer GitHub token or run `git pull` on a customer machine.
- Test no update, newer/older/equal/RC versions, wrong hash, offline/rate limits,
  interrupted transfer, file locks, permission failures and preservation of the
  old executable. Publish a manifest only for an authorized actual release.

References: [Windows Setup options](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/windows-setup-command-line-options),
[Windows 10 release/lifecycle information](https://learn.microsoft.com/en-us/windows/release-health/release-information),
[upgrade paths](https://learn.microsoft.com/en-us/windows/deployment/upgrade/windows-upgrade-paths),
[GitHub latest release](https://docs.github.com/en/rest/releases/releases#get-the-latest-release).
