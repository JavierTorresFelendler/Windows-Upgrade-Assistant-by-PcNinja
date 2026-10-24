# Development handoff

Updated: 2026-10-24. This is sanitized project context, not a backup of Codex chats.

Reusable architecture, diagnosis and verification know-how lives in
[AI maintenance playbook](AI-MAINTENANCE-PLAYBOOK.md), independently of any
assistant's personal memory or skills installation.

## Current authorized work: portable Beta update demonstration

The owner approved Beta 1 then Beta 2 testing BEFORE any official release.
Keep RC8/main and all existing assets unchanged. Continue in Draft PR #13.
This scope supersedes the Program Files/replacement design below; it does not
authorize merging, stable/RC9 publication or publishing Beta 2 before Beta 1 is
opened/tested by the owner.

- Versioned EXEs saved beside the running tool; no original overwrite, old-name
  rename, numbered copies or Program Files fallback. Private anchored staging
  stays below that same folder. Existing exact-version files must pass validation.
- Metadata-only discovery may run at startup. EXE transfer needs explicit GUI
  **Download and run the new version** consent or CLI **app-update run --yes**.
  Keep the unreleased install verb only as an alias; no installer is involved.
- Explicit Beta channel selects only numbered Beta releases; stable/RC selects
  stable only. Current source version is 1.1.0-beta.1, not a replacement RC8.
- Offer includes Regular/Critical badge, bounded plain-text publisher message
  and exact version's GitHub notes link. Critical remains optional, never forced.
  Release notice is source-controlled in docs/updates/{version}.json.
- Preparing first Beta prerelease from the exact CI-passed PR commit is authorized.
  Beta tags may remain unmerged, but require matching successful PR CI; stable/RC
  still require main ancestry. All publication remains draft/upload/verify-first;
  Beta never changes stable/latest. No Windows Setup/reboot/live app update on
  the development machine.
- Beta 1 is published as an immutable prerelease, tag `v1.1.0-beta.1`, source
  `1e25bac7f324756ff313a573877f075becab7785`. Release ID `422891503` has exactly
  four assets. EXE `Windows-Upgrade-Assistant-By-PcNinja-V1.1-Beta1.exe` is
  73,084,772 bytes, unsigned, SHA-256
  `ec654f1f3e3b28b947da6d89af2a5d6f0ebd8a76e4e49625cd6fea239a80648f`.
- Local Core/headless CLI/client GUI/packaging suites pass. PR CI `40117997759`
  passed including elevated private-workspace and real disposable-byte versioned
  publish/reuse/no-delete launch-guard tests. These do not execute the app helper.
- Tag workflow `40118262354` passed source gate/build/tests, then failed its final
  draft lookup: the tag API hid the draft and draft asset URLs were temporary
  `untagged-*` URLs. Verified the existing draft ID and every immutable uploaded
  asset against independently downloaded CI artifact `12569628129`, then published
  that same release as prerelease/non-latest. No tag or asset was replaced.
  Publisher-only follow-up uses immutable release-ID checks and permits temporary
  same-repository URLs only before publication; published/client URLs remain pinned.
  Offline fixtures cover both behaviors and reject temporary published URLs.
- Downloaded all four assets again from the public release; exact API sizes,
  SHA-256 digests, source/product version and actual portable read-only CLI checks
  pass. Live metadata-only check returns `APP_UPDATE_CURRENT` with setupStarted
  and readyForRestart false. Stable/latest remains absent (404); RC8/main/tag and
  RC8 assets are unchanged. A bare uncaptured invocation in the development tool
  hit an invalid console handle; captured stdout (the test/JSON contract) passes.
  Investigate that hosting edge case separately; do not claim it fixed here.
- Remaining acceptance: owner opens Beta 1 on a VM from an ordinary writable local
  NTFS folder, then authorizes Beta 2 publication for real consent/download/helper/
  close/relaunch testing. No Beta 2, RC9, stable, main merge, Windows Setup or reboot
  was performed. The later publisher-only checkpoint does not alter Beta 1 runtime.

## Historical RC8-era updater checkpoint (superseded destination design)

### Current development: application updates (Draft PR #13)

The owner has now authorized updater implementation while continuing non-English
Windows-upgrade tests. This supersedes the earlier design-only hold for #13,
not the hold on merging, publishing or changing RC8. Refreshed the existing
`design/application-updates` branch with reviewed RC8 main; keep one PR/history.

- Shared Core discovers only this project's latest stable GitHub release and
  same-release manifest. Bounded metadata/redirect/transfer policy, strict release
  identity, exact size/SHA-256, executable metadata and signed-publisher continuity
  fail closed. An unsigned release's checksum is not publisher authentication.
- GUI discovery is nonblocking and separate from Windows-media diagnostics.
  Installation needs explicit consent; CLI adds documented `app-update`
  check/status/install commands without OS restart authorization.
- The successor helper installs a controlled local Program Files portable copy.
  An original outside that folder remains untouched. Private staging, anchored
  paths, parent-process-bound READY/GO handoff and recoverable atomic replacement
  avoid elevated replacement of a user-writable source path. Relaunch has no
  arguments; CLI installation does not reopen a GUI.
- A secure machine-wide operation gate serializes app replacement with Core ISO
  preparation/validation/download/Setup. Repeat external Setup and pending-restart
  checks before handoff/apply. Historical upgrade records are not permanent locks.
- Centralized version identity in `Directory.Build.props`; development remains
  `1.1.0-rc.8` with source-commit provenance, not a newly published RC. Stable
  packaging is draft-first, verifies all immutable assets, and updates latest last.
  No stable manifest, tag or feed has been published by this implementation work.
- Local Release warnings-as-errors Core, headless CLI, client GUI and packaging
  fixtures pass. Helper request/expiry/path/handshake bounds and actual atomic
  file move/replace/restore fixtures pass, including failure and lock cases.
  The locally built portable test EXE passes read-only CLI/help/completion checks.
  Tests use fake network,
  unique Local gate objects and harmless temporary files, not live app replacement
  or Windows Setup. Elevated ACL fixtures and true two-stable-version update
  acceptance still require separate evidence. Do not describe this as shipped.

- Implementation checkpoint `6df4cdb03b2ab900b4259cc618e7d7ce071deb3d` is committed
  under the owner, pushed and remote-SHA verified. PR #13 remains Draft and
  mergeable. CI run `40110660239` passed all steps, including elevated protected
  temporary-folder/ACL/anchor/cleanup fixtures (no Program Files writes). Local
  non-admin SKIPs above remain honest, separate from this hosted native evidence.
  A follow-up also proves read-only inspection accepts the actual built product
  host, preserves subcommand help, and distinguishes helper deadline from cancellation.
- Follow-up CI `40110882263` caught an existing global two-argument help guard
  rejecting the new three-argument updater help form. Correct only that recognized
  help route, retain rejection of unrelated extra arguments, and add real
  `CliRunner` regression coverage before rebuilding the final artifact.

- Final runtime candidate `cf84c864d8111b20400b0577761fe15f0abb5d24` is pushed and
  remote-SHA verified. CI [40111065195](https://github.com/JavierTorresFelendler/Windows-Upgrade-Assistant-by-PcNinja/actions/runs/40111065195)
  passed all steps, including the real updater help route and elevated temporary
  native filesystem fixtures. Local client GUI tests also passed independently.
- Downloaded [test artifact 12566014956](https://github.com/JavierTorresFelendler/Windows-Upgrade-Assistant-by-PcNinja/actions/runs/40111065195/artifacts/12566014956)
  separately; verified three files, source, tested merge
  `c20376ce9c9544be5fe42fb24438bd5ff03fafad`, product informational version,
  exact SHA256SUMS and 73,080,239-byte EXE size. SHA-256:
  `b052a1f55e61215bfa97aafe21fc0a2475c8784af9dd8e618d001d5775e36da8`.
  Authenticode is `NotSigned`, not a signed-publication claim. Downloaded EXE
  passes actual read-only CLI/help/consent/completion checks. A real metadata-only
  GitHub check returned normal `APP_UPDATE_NO_STABLE_RELEASE`, with no executable
  transfer, app installation, Windows Setup or restart. This does not prove the
  future stable download/helper/elevation/relaunch integration.
- RC8 main/tag, local release EXE and all three remote release asset IDs/digests
  remain unchanged. This final handoff is documentation only; the linked runtime
  candidate remains the test reference, not a rebuilt or republished RC8.

Await owner review and separately authorized disposable-VM/two-stable-version
acceptance. Do not merge, mark ready, publish a release, replace RC8 or run an
actual updater installation on the development PC. Media preparation #9 stays
deferred. See [application updates](APPLICATION-UPDATES.md).

### Published baseline (unchanged)

- Owner reports the current test application is working well and explicitly
  requested another RC before application-update planning or implementation.
  This authorizes consolidation and RC8 publication, not additional features.
- Merged #20 as `6dd920b41efae51f5e8f16daa7360c9a1bf06e1c` and #21 as
  `c3e67f7ff2b0973d3d74c21cbec0a486d60ad43b`. #21 was updated against main and
  CI run `40101220921` passed. Its integration tree matches the accepted
  `bd0f2ee` test candidate. Closed #19 as superseded after verifying its required
  behavior is covered by #21; no duplicate routing implementation was merged.
- RC8 preparation changes version metadata to `1.1.0-rc.8`/file `1.1.0.8`,
  the HTTP version identifier and current release documentation only. Preserve
  the fixed EXE filename, original assets/links and all previous build files.
- Release gates: warnings-as-errors local Core/headless CLI/client GUI checks,
  protected-PR validation, merged-main tag/version agreement, then independently
  download Release assets and verify commit/version/SHA-256/size/read-only CLI.
  Record final run/artifact evidence on the release PR; do not execute Setup on
  the development machine or mistake CI for completed migration acceptance.
- Local rc.8 Core/headless CLI/client GUI suites and actual-EXE read-only checks
  passed, with zero build warnings/errors. The local native journal fast path
  explicitly reported SKIP (non-admin); synthetic proof/fallback fixtures passed.
  Independent review found no functional drift in the release-only source diff.
- [RC8 notes](RELEASE-RC8.md) and [verification matrix](RC8-TESTING.md) describe
  scope and remaining field cases. The owner reports stable operation, but did
  not restate a complete per-profile/source test matrix for this exact binary.
- Application self-update (#13, design only at RC8 publication) and media preparation (#9) remain
  outside RC8. Updater work starts separately only after this release checkpoint.

The development sections below are historical checkpoints. Their earlier
no-merge/no-release holds are superseded only by the RC8 approval above; they do
not authorize unrelated feature changes or a later stable release.

## Previous development: matched ISO downloads and manual Server guidance

### Owner-approved automatic preflight and GUI next-action follow-up

- Reviewed actual download/launch order before editing. Full-profile automatic
  preflight already precedes HTTP/destination creation; retained the configured
  owner URL, all eleven edition IDs, transport/cookies/TLS/provider fallback,
  exact-name reuse and automatic consent/download/validation/upgrade sequence.
  Added real-entry synthetic no-HTTP/no-destination regressions and GUI pre-consent
  recheck. This does not assert remote bytes match configuration before download.
- Add registered Downloads-first ISO picker with unchanged exact-file checks,
  safe fallback and CWD restoration. Manual source stages get exclusive browse,
  Validate, image-frame and Start cues; idle cues never override red Cancel or
  running Setup. Auto retains Start with no added manual Validate. Clear displayed
  prepared state before rechecking media; failed/cancelled checks cannot imply Ready.
- Complete #19's missing CLI error fallback-page text without changing its
  no-browser quiet/JSON rule. #19 is not merged/closed here. Update Unreleased
  changelog for #20/#21; no new version or release approval is implied.
- Verification: synthetic unsupported preflight, all existing Core/transfer/mount
  contracts and new GUI/CLI fixtures. External effects are fake; no real provider
  ISO transfer, ISO attach/detach, Setup or reboot during development. Current
  branch remains Draft #21 stacked on #20; existing test EXEs remain immutable.

### Scope and owner field update (2026-10-24)

- Owner ran the corrected `0b1e0f2` test EXE on the previously blocked LTSC VM,
  selected the ISO and reported successful preparation checks/start. This confirms
  the readiness blocker is no longer reproduced in that trial; it is not a final
  completed migration/apps-and-files preservation claim. Windows 10 Pro in Spanish
  is under owner test, with outcome pending.
- Freeze new feature scope around stability and safety. Media preparation design
  #9 remains deferred. Application self-update through GitHub is the only planned
  next new capability, using the design in #13 after it is refreshed. That PR is
  still design only, not a merged/working updater. See [current roadmap](ROADMAP.md).
- Documentation-only follow-up adds explicit Massgrave catalog/website/project
  credits. Main-site GitHub link and catalog source repository were verified;
  current WinUpdate V2 and Smart Office update manifests were inspected as
  references. Do not copy their host-specific updater/replacement code blindly.
- This follow-up changes no application code, provider URL, version, compiled
  active test EXE or published artifact. No merge, RC/stable tag or Release.

### Owner-approved LTSC correction (same Draft PR #21)

- Owner reproduced an inconsistent readiness state on Windows 10 Enterprise LTSC
  21H2/build 19044.1288 using Hebrew Windows 11 Enterprise LTSC 2024 media: image
  inspection succeeded and advertised Start, but a separate LTSC host ban disabled
  Start; Core and CLI also contained a blanket LTSC host ban. Manual Windows Setup
  offered to retain apps/files and was progressing when reported. Final migration
  and data-preservation acceptance are still pending, not claimed as passed.
- Replace divergent host gates with shared `ManualUpgradePolicy`: x64 Windows
  10/11 Client, including LTSC, can use matching manually supplied media. Exact
  edition/N/IoT, language, architecture and image profile checks remain; no channel
  conversion, Server execution, automatic LTSC transfer or guessed LTSC catalog
  match is enabled. Windows 10 target POC build-order policy stays unchanged.
- GUI uses one detected profile for host policy and image guidance. Unsupported
  hosts show their Core blocker instead of Start instructions; host eligibility
  is logged for diagnosis. CLI status/preflight and final Core launch share the
  same policy. Consent, storage, signature, answer-file, restart and final-media
  revalidation remain unchanged. No real Setup/ISO/mount/reboot is run in tests.
- Added regression first failed on the old implementation's disabled LTSC Start.
  New Core/GUI tests cover Hebrew LTSC, edition/language rejection, automatic
  exclusion, unsupported-host guidance, Server/architecture/type blocking and
  unchanged command/restart/quiet profiles. New test artifact evidence follows
  in the PR; original PR21 and RC files remain untouched.

### Original matched-download implementation

- Approved separate Draft PR on `feature/matched-iso-downloads`, stacked on
  `feature/iso-validation-reuse` (#20). It includes the eleven-edition OEM routing
  policy requested in #19 without changing #19, #20 or main. CI includes the
  stacked base branch; no merge, ready-for-review, RC/tag, Release or replacement
  of published/tested assets is authorized.
- Full-profile matching prefers configured PcNinja media; unmatched systems get
  on-demand exact public landing guidance from Client/Server catalogs. Browser
  download requires manual exact-ISO selection and validation; no host challenge
  bypass or automatic continuation. `downloads [--json]` is read-only. `status`
  stays offline; automatic transfer is full-profile gated in GUI and CLI.
- Server has download/inspect guidance and explicit, freshly checked manual CMD
  copy only. Start/quiet/restart remain disabled, Core execution guard unchanged.
  No real Server command is run or implicitly authorized by a recommendation.
- Implementation plan, release-family/26H1 handling, safe probe, fixture matrix,
  extension rules and remaining owner VM gates: [matched ISO downloads](MATCHED-ISO-DOWNLOADS.md).
- Local warnings-as-errors Release builds and Core/headless CLI/client GUI suites
  passed, including the existing regression suites. The GUI suite passed twice
  with fake external effects. The non-admin native journal proof still reports
  SKIP; do not describe a synthetic ISO cache hit as a real media test.
- A bounded live HTML-only probe matched es-ES Pro, es-MX Pro N and en-US Enterprise
  to the expected Consumer/Business 26H2 catalogs, and es-ES Server Standard to
  Server 2025. It did not follow landing links or download media. CI/artifact
  evidence is recorded in the Draft PR; actual provider transfer, ISO attach/detach
  and upgrade acceptance remain unproven by this development work.

## Previous development: session-scoped exact ISO validation reuse

- Owner approved a separate implementation/test-EXE PR on
  `feature/iso-validation-reuse`, based on main `0e738b5` (merged #18). #19's OEM
  browser routing remains a separate unmerged PR awaiting owner acceptance.
- Share Core preparation between GUI manual ISO, Automatic existing-file reuse
  and CLI. Reuse only after fixed-NTFS unchanged-file journal/CLOSE proof and
  exact device/volume/path mount binding; no size/time-only shortcuts or idle
  handles. Fresh signature/answer-file checks and final Setup gates remain.
- Add clear checksum/image-reading progress and already-mounted versus newly
  mounted logs. Source/index selection, consent, restart/quiet/results, actual
  links, logos and existing release files remain unchanged.
- Local warnings-as-errors Release builds and full Core, headless CLI, client GUI
  and actual-EXE read-only CLI checks passed. This includes stale-progress rejection,
  consent/quiet/restart/image cues and logo/public links. The optional real tiny-file
  native proof reported SKIP on this non-admin client: journal/CLOSE evidence was
  unavailable; operation lock/release and synthetic cases passed. Do not describe
  this as a confirmed native cache hit. CI/artifact evidence follows in the PR;
  real ISO/VM acceptance remains the owner's next gate.
- See [design, limits and tests](ISO-VALIDATION-REUSE.md). No real provider transfer,
  ISO mount/detach, Windows Setup or reboot was invoked during development.
  No merge, RC/tag, Release or replacement of any existing EXE is approved.

## Merged #18 follow-up: state-aware installation-image guidance

- Owner approved replacing an empty image-selector display with a cue that
  complements, rather than overwrites, existing incompatibility/error messages.
  Continue on the existing development branch/Draft PR #18; RC7 is unchanged.
- Distinguish unvalidated media, compatible images awaiting selection, no
  compatible image and an actual selected image. Cues are owner-drawn display
  text only, never an item/index or consent to continue. Preserve the existing
  unique-match and same-source selection policy and all Core/CLI behavior.
- Inspect previously returned generic selection instructions before checking for
  zero compatible images. Report existing Core rejection reasons immediately,
  including selected-image warnings, in the existing persistent notice area.
  A mount/read/validation failure remains its original error, not a no-match.
- Verification: add client GUI fixtures for actual empty-row drawing, real-item
  identity, incompatibility reasons, Inspect results, Refresh/source change and
  independent error/details preservation. No Setup, mount or download is needed.
  The new cue fixture first failed on the old implementation, then passed with
  the fix. Local warnings-as-errors Release builds, full Core/headless CLI/client
  GUI suites and actual EXE read-only CLI checks passed. The rendered normal
  preview was inspected; original logo/links and quiet/restart behavior remain.
  CI and downloaded portable-artifact checks remain separate delivery gates.
- No merge, new release/tag or replacement of published/tested EXEs is approved.

## Current feature: quiet Setup and remote result contract

- Owner requested optional quiet Windows Setup and clarified that CLI failures
  need machine-readable identifiers for remote/scripts/BigFix, not error popups.
  Branch `feature/quiet-setup-automation`, based on published RC7 main `d502b60`.
  No new release, tag, merge or changes to RC7 assets are authorized.
- Reuse existing preparation-result handling. Add opt-in /quiet to shared command
  construction; CLI --quiet and --no-restart remain independent. Real quiet CLI
  needs explicit --yes, never opens browser fallback and emits no GUI dialogs.
- GUI quiet checkbox selects/locks automatic restart and restores its prior state
  when unchecked. Consent includes silent Setup and its per-attempt snapshot.
- Preserve existing process exit codes 0-8 and JSON fields. Add schemaVersion 1,
  stable resultCode, readyForRestart, message/build/log identity and native Setup
  integer/hex. Status/validation/dry-run/failures never authorize reboot. Only
  actual preparation exit 0 with --no-restart yields the ready state.
- Test plan: command matrix, headless result-presenter fixtures in CI, read-only
  portable CLI checks, client GUI checkbox/consent/state and existing safety suites.
  No developer Setup launch/reboot. Owner VM/remote-agent acceptance remains open.
- Local Release warnings-as-errors builds and Core/headless CLI/client GUI suites
  passed, including quiet/restart combinations, native failure/uncertain outcomes,
  every assistant failure group and GUI preference restoration/consent invalidation.
  Local portable EXE CLI checks also passed (help/JSON/quiet consent/Tab completion/
  invalid options/subsystem/size). CI and downloaded-artifact verification remain
  separate gates before handoff. The PowerShell wrapper example explicitly
  propagates the captured EXE exit code for management-agent use.
- See `CLI-AUTOMATION.md`, updated offline help/completion and `UI-MESSAGES.md`.
  Existing app self-update/ISO-copy designs remain outside this feature.

## RC7 authorization and scope

- On 2026-10-21 the owner authorized combining the reported resize-rendering fix
  with another ready PR and building RC7; otherwise work should wait. PR #16 was
  the suitable implemented, green, conflict-free change. It is now merged as
  `ad9782e`; PRs #9/#13 remain design-only and are not release candidates.
- Branch `release/rc7-resize-redraw` adds the narrowly scoped card redraw fix,
  client GUI regression coverage and RC7 metadata/documentation. Do not merge
  until checks pass. Tag v1.1.0-rc.7 only on reviewed merged main source, then
  verify the uploaded release binary/commit/hash/CLI/size before handoff.
- Preserve original mascot/icon/public links and all Core/CLI operational logic.
  Only the HTTP user-agent version label changes outside the GUI. Popup flow,
  validation reuse, mount/eject investigation, app self-update and ISO preparation
  remain deferred. Existing RC6 and older releases stay unchanged.
- See `RELEASE-RC7.md` and `RC7-TESTING.md`. Owner regression on the originally
  affected display/DPI remains distinct from local client fixtures and CI.
- Local verification: warnings-as-errors Release builds and full Core/client GUI
  suites pass. The new fixture first failed on the old plain Panel with missing
  ResizeRedraw, then passed after the scoped panel subclass was added, including
  full invalidation/paint on live resize and maximize/minimize/restore. No real
  Windows Setup, reboot or ISO download was performed.

## Merged main-window copy (PR #16; historical test checkpoint)

- The owner approved the main-window mockup and requested a separate PR with a
  portable EXE for testing. Branch: `ui/main-window-copy-review`, based on RC6
  main `77aa2b8`. No merge, new RC, stable release or replacement asset is approved.
- Scope: concise headings/descriptions/control captions, remove duplicate source/
  computer kickers, keep the two source choices and Start/Cancel behavior. Wrap
  the longer computer/settings heading within the existing right panel.
- Preserve the original mascot/icon and all real website/manual download link
  handlers and targets. No Core, CLI, download, consent or Setup-policy changes.
- The exact approved copy and deliberate differences from illustrative preview
  data are recorded in `UI-MESSAGES.md`. Media validation remains a requirement,
  not a hardcoded result; actual hardware values still come from the computer.
- GUI fixtures cover new headings, selected/unselected captions, one original
  embedded logo and public URL constants, alongside existing safety tests.
  Local Release builds passed with warnings as errors, Core smoke fixtures and
  Windows-client GUI checks passed, and the rendered normal/minimum-window
  previews were inspected. Short summary values avoid minimum-width clipping.
  No Windows Setup or real download was performed. PR CI supplies the separately
  identified EXE/checksum/BUILD-INFO artifact; verify it before owner handoff.
- Popup cleanup/wording, manual-validation reuse, eject investigation and app
  self-update remain separate follow-ups. Published RC6 stays frozen.

## RC6 publication checkpoint

- On 2026-10-20 the owner explicitly approved merging the tested changes and
  publishing **1.1.0-rc.6**, then returning to remaining work. This supersedes
  older RC5/PR #12/#14 release holds below, not the stable/self-update hold.
- Windows 10 PR #12 merged as `a51797f79589717eaa4d485368f12b84e919331f`.
  Branding/outcome PR #14 merged as `ad346b3c172379b388862000b7a9e0aa7fdffe37`
  after retargeting to main and passing fresh integration CI `39360400032`.
- Release branch `release/rc6` updates metadata to `1.1.0-rc.6` / file version
  `1.1.0.6`, documentation and version-neutral GUI checks only; no new upgrade
  command, downloader behavior, popup-flow rewrite or ISO preparation feature.
- Publish only after the release-preparation PR passes checks and is merged.
  Tag `v1.1.0-rc.6` must point to that reviewed main commit. The existing tag
  workflow builds the fixed-name EXE plus SHA256SUMS.txt and BUILD-INFO.txt,
  enforces the 100,000,000-byte cap and publishes a GitHub **prerelease**.
  Verify the uploaded asset hash, version/commit, size and read-only CLI before
  reporting completion. Preserve all prior release tags/assets and frozen EXEs.
- RC6 includes PRs #8/#10/#11/#12/#14. Owner-reported Windows 10/11 stability
  and the explicit 19043 -> 19045 apps/files check are acceptance evidence,
  not proof that all customized images or migration paths work.
- Still outstanding: popup/flow cleanup before exact wording review, safe manual
  validation reuse, mount/eject investigation and application self-update design
  in PR #13. ISO copy/clean/detach remains another project's variation (PR #9
  design only). Do not merge those designs or activate an updater/feed here.
- See [RC6 notes](RELEASE-RC6.md) and [verification](RC6-TESTING.md). Published
  release provenance is recorded in BUILD-INFO and the release-preparation PR.

## Historical RC5 baseline and release hold

- Published baseline: `v1.1.0-rc.5`, source commit
  `408628f67469e263be69825faccd046994148046`.
- The owner authorized staged merging/development on 2026-10-17 until real VM
  tests require participation or work reaches Internet application self-update.
  PR #8 was squash-merged as `d2f671c263d90458eaace871b55209337ab62a24` after
  passing PR validation. Published RC5 EXE/tags remain unchanged.
- After owner-reported Windows 10 to early Windows 11 success and renewed merge
  authorization, PR #10 was squash-merged as
  `3ebd17e463bd090ae16b2a74797c7ddd047ef35f`. Its CI source checkpoint was
  `ec7830c084222cd89b91a83b55fb0c38ca3545ee` (run `39051844550`).
- Stop for owner testing of runtime changes before promoting unverified flows.
  Do not publish the first stable release or implement/activate self-update yet.
  The owner wants a first official version, then a distinct higher-version
  release to test Internet updating (it may contain no functional changes).
- Approved source changes must still be committed/pushed to a development branch
  with a Draft PR. Local-only work is not an acceptable completed checkpoint.
- Product versions remain RC5 until a separate release decision. CI artifacts
  from a development commit are test builds, not a replacement RC5 release.

## Current follow-up: product identity and read-only rollback diagnostics

- Branch `feature/upgrade-outcome-and-branding` is stacked on unchanged Windows
  10 PR #12 source `bdaee5e`. Its PR targets that feature branch. Do not merge it
  into the frozen owner test source while tests are in progress.
- Owner confirmed Windows 10 Pro **21H1 (19043) -> 22H2 (19045)**, including
  intact working apps/files. App logs show manual extracted-folder ESD index 1,
  `/noreboot` and preparation exit 0; owner supplied post-restart system info.
  This is not automatic-download coverage or proof of every Windows 10 path.
- A distinct failed trial has completed rollback **0x800700B7 - 0x2000A**:
  ReAgentOld.xml collision during Safe OS relocation. Neither media corruption
  nor the origin of the duplicate is established. Raw VM logs are not committed.
- Core reads bounded, correlated Panther/Rollback evidence and exposes
  `RollbackObserved`/`SetupFailure` in existing GUI notices/details and CLI status.
  No Recovery cleanup, Setup argument change or forced reboot. Missing or
  uncorrelated evidence stays unknown. Last-Upgrade.json keeps process-stage
  evidence; status computes the observation without overwriting that file.
- Display name: **Windows Upgrade Assistant By PcNinja**. Fixed upcoming EXE/
  Release asset: `Windows-Upgrade-Assistant-By-PcNinja.exe`. No GUI RC/version
  branding; metadata/startup log/explicit CLI diagnostics retain release/commit
  identity. Historical RC5 assets and the PR #12 test EXE stay unchanged.
- Core, Windows client GUI and read-only CLI checks passed locally, including
  warnings-as-errors builds, bounded UTF-8/UTF-16 and stale/source/index/active
  Setup fixtures, persistent notices and branding. CI and owner acceptance of
  this follow-up remain separate gates. No actual Windows Setup was run locally.
- Owner wants popup/flow cleanup before exact wording review. Do not remove
  safety/confirmation dialogs or add a wizard as part of this change.
- ISO copy/extraction/clean/detach is **outside this standalone project**, for
  another product/variation. PR #9 is reusable design context, not a required
  outstanding standalone feature. Do not implement or merge it here.
- PR #13 (`design/application-updates`) is design only for updating this EXE
  through verified GitHub Release assets, not Windows OS updates. No updater,
  feed, stable tag or Internet replacement is activated. Its design is not part
  of this stacked branch. No merge/release has been performed.
- Details: [product identity](PRODUCT-IDENTITY.md) and
  [outcome diagnostics](SETUP-OUTCOME-DIAGNOSTICS.md). Older sections below are
  historical checkpoints, not renewed authorization for deferred features.

## Previous checkpoint: owner-scoped manual Windows 10 POC

Branch: `feature/windows10-target-upgrades`, based on merged PR #11
(`e356cca0d5cb71cddca550a807e1ff8815b62d82`). Published RC5 and prior test EXEs
are unchanged. PR #11 CI was `39058458738`, source `f4443b7`.

- The owner explicitly narrowed the experiment: reuse image selection, recognize
  Windows 10 and select the appropriate command, without determining older/equal/
  newer Windows 10 versions. This supersedes the stricter target-build plan;
  it does not authorize automatic media extraction, downgrade promises or clean
  installation. No offline-hive extraction/version resolver was added.
- Core recognizes client Windows 10 image metadata (10.0 builds 10240-19045),
  reuses the exact source/index and standard `/auto upgrade` profile without
  `/product server` or `/eula accept`. Windows 11 keeps the old profile and build
  comparison. Windows 11 -> 10 and unsupported families remain blocked.
- Windows 10 trials require manual ISO/mounted/extracted/network media. Shared
  download validation refuses Windows 10 images, including mixed media; the
  existing automatic source is Windows 11 only in both GUI and CLI.
- Preserve edition/UI-language/x64, signature/DISM, storage, answer-file and final
  prelaunch gates. Setup must approve keeping apps/files or refuse; no quiet,
  clean, data-only, disk selection or forced-reboot fallback is introduced.
- GUI shows the experimental policy beside the image and in manual confirmation.
  CLI help/validation/dry-run expose `setupProfile`, `buildOrderChecked` and a
  policy notice, with profile/order also in successful upgrade JSON.
- Windows 10 raw image build is not a verified feature-release target. Existing
  history fields remain, but Windows 10 attempts cannot automatically become
  TargetWindowsObserved. Exit 0 still means preparation only; verify manually.
- Local Core/GUI/CLI tests cover accepted older/equal/newer Windows 10 fixtures,
  cross-family and metadata rejection, exact WIM/ESD/SWM arguments/restart policy,
  automatic-source blocking, Windows 11 regression and confirmation warnings.
  No Windows Setup was run on the developer. CI and disposable owner VM tests
  precede promotion of this changed upgrade flow; keep its PR Draft for testing.
- Acceptance and rollback: [Windows 10 POC checklist](WINDOWS10-POC.md). Preserve
  an old known-good EXE and VM snapshot; report code/log evidence without posting
  raw customer data. Stable/self-update holds and deferred PR #9 are unchanged.

## Merged source: manual-restart guidance (PR #11)

- When Setup returns 0 with automatic restart disabled, show an information
  dialog titled **Restart required** with an OK button: "Upgrade preparation is
  complete. Restart your computer to continue the upgrade."
- Keep the same explanation in the readiness panel through Refresh, plus a
  waiting-for-manual-restart status. OK only dismisses the dialog; it never
  restarts the machine. Do not display this popup for failure, cancellation,
  unconfirmed transition or automatic-restart mode.
- CLI prints the same instruction for successful `--no-restart`; successful
  upgrade JSON adds `manualRestartRequired`. Existing exit codes and
  PreparationCompleted semantics remain: exit 0 is not final migration proof.
- Core policy tests cover both restart settings and success/failure/transition
  codes. Client GUI tests exercise the actual completion presenter with an
  injected information-dialog sink; no Setup or reboot is invoked. Existing
  Core/GUI/CLI regression suites and warnings-as-errors builds still apply.
- This is a presentation-only follow-up to the owner-tested flow; it does not
  change Setup arguments, launch, storage or answer-file gates. Keep the prior
  test EXE and published RC5 immutable; CI produces a separately identified EXE.

## Remaining scope and explicit deferrals

| Work | Current disposition |
| --- | --- |
| Automatic consent, download handoff and readable diagnostics | Merged in #10; broader source/language/automatic-download field matrix remains distinct from one folder upgrade |
| Manual-restart explanation | Merged #11; Core/GUI/CLI tests and CI passed |
| Windows 10 target upgrades and equal-version repair | Experimental manual POC on the current branch; no Windows 10 build-order comparison by explicit owner request; CI and VM acceptance pending, not yet in published RC5 |
| Safe ISO copy/clean/detach | Owner explicitly deferred; keep design-only PR #9 Draft for the future EXE shipped inside customized ISOs. Do not merge/implement it as part of this pass |
| General manual Inspect/Start validation reuse | Deferred pending reliable identity/invalidation design; no size/timestamp-only cache |
| Mount ownership/eject-lock cleanup | Investigation open; no proven locking process or general safe-detach fix |
| Internet application self-update | Design only in design/APPLICATION-UPDATES.md; first stable must already contain the future updater, then test a higher official version with owner participation; no active feed/updater yet |

### Latest owner field evidence

- The owner confirms Windows 10 to early Windows 11 worked after a manual
  restart. The corresponding app record shows original build 19043, target
  22621 Professional, exact folder/SWM image index 1, `/noreboot`, and Setup exit
  0 after about 14 minutes. The initial impression of a crash was not supported
  by that attempt's app logs; root Setup preparation completed without auto-reboot.
- This is owner-reported completion, not independent confirmation of every app
  or file. It does not close the distinct earlier morning Appraiser crash.
  Do not conflate old Panther logs with this later attempt.
- An earlier Windows 10 target test first hit en-US/en-GB mismatch and unsafe
  secondary-media gates; later it reached the hardcoded Windows 11 target gate.
  A valid matching Windows 10 target still cannot run in the current product.
- The log also confirms successful selected-folder Autounattend.xml deletion.
  No raw VM logs, JSON or screenshots are committed to this public repository.

## Merged source: automatic consent and actionable diagnostics

Merged PR #10: `fix/automatic-consent-and-diagnostics`.

- Automatic mode asks once before transfer/reuse, covering validation, compatible
  in-place upgrade and the selected restart policy. A unique matching image then
  continues without a second assistant question. Cancel defaults to No.
- Consent is local to that attempt and exact returned source object; manual
  sources, a later Start, ambiguous images or changed restart policy require the
  normal confirmation. Browser fallback stops the attempt rather than carrying
  unattended consent into a later manual selection.
- Shared Core returns the exact successful download's validated mounted source.
  GUI and CLI consume it instead of immediately hashing/mounting/enumerating the
  download again. Final source/image/signature/answer-file/storage checks remain.
  Manual Inspect then Start caching is NOT implemented: timestamps/size alone are
  not sufficient identity evidence. Mount ownership/detach cleanup is NOT fixed.
- Decode bounded, DTD-disabled PowerShell CLIXML error records, suppress progress,
  retain raw stderr and exit code in details/log. A 30-second drive-discovery
  timeout explains Refresh/manual mounted-folder selection, not presumed ISO
  corruption. Log exact requested ISO and resolved volume. Helper working directory
  is Windows System32; this does not prove the unresolved eject problem is fixed.
- Failure and no-compatible-image explanations remain in a multiline warning
  panel through Refresh. Low-space guidance names the actual Windows volume,
  available/required/missing space and action, independently of ISO destination.
- Automated verification: Core smoke tests, bounded CLIXML/DTD/space fixtures,
  Windows client GUI consent/invalidation/real rejected Start/persistent-warning
  checks, existing language/selection/cancel tests and read-only CLI checks.
  Builds use warnings-as-errors. No Windows Setup was launched locally.
- Delivery/owner acceptance: see [automatic-flow test checklist](AUTOMATIC-FLOW-TESTING.md).
  A green CI compiles GUI checks but cannot replace their Windows client run or
  a disposable-VM upgrade. Owner feedback and merge authorization were received;
  untested matrix cases remain release-validation work, not implied passes.

## Merged source: language-aware automatic download

Merged PR #8: `fix/language-download-guidance`. The older notes below preserve
the investigation history; current implementation status above supersedes any
old statement that all future work remains unauthorized.

Implemented in source, not the published RC5 EXE:

- The configured automatic ISO language is `en-US`. Disable automatic download
  for a different system-default UI language, including `en-GB`, with a persistent
  explanation showing system and automatic-image languages.
- Keep matching manual ISO/mounted/folder/network media available without an
  English-only restriction. Existing edition/x64/Windows 11/build/safety gates
  remain; this does not imply every language has been field-tested.
- Apply the same gate in Core and CLI before a download; CLI validation failure
  uses exit code 3. Read-only status reports language, availability and reason.
- Manual PcNinja downloads use `https://iso.pcninja.pro`. Retain Microsoft's
  official manual option and recommend it for standard Microsoft Windows media.
- Keep the internal automatic transport separate and unchanged.
- Show both detected languages in a selected-image mismatch warning next to the
  image selector. Keep technical diagnostics available separately.

## Verification for this source change

Passed locally on Windows 11, without launching Windows Setup:

- Core smoke tests, including simulated transfers and existing media safety gates.
- Synthetic language-policy tests: en-US accepted case-insensitively; he-IL,
  en-GB, ru-RU, ar-SA, ja-JP and de-DE denied automatic download but matching
  manually supplied image metadata accepted. Unknown language denied.
- GUI checks: source exclusivity, Start/Cancel, mismatch text, manual alternatives
  remain available, and busy-state transitions do not re-enable a denied download.
- CLI version/help/status JSON/argument rejection/completion/subsystem checks,
  including language-policy status fields and manual-download guidance.
- Debug builds of GUI and Core test projects with warnings treated as errors:
  zero warnings/errors. No distributable release was produced.

GitHub PR validation passed for source checkpoint
`2d471e39e01c3fc16449fccd8032ceadf8a91205` in Draft PR #8, run
`39003768728`. A later documentation checkpoint has its own CI run; verify the
current head rather than treating this result as approval for another commit.
Owner field tests with matching non-English media are still required. Synthetic
metadata acceptance is not a real upgrade result.

## Recent owner evidence and pending follow-up

### New successful round and proposed delivery order

- The owner reports a completed further round from Windows 10 through Windows 11
  26H2 with successful results and is satisfied with the last test executable.
  Exact artifact identity and per-source coverage were not restated; verify before
  marking a particular PR accepted. Earlier failure investigations remain open.
- Requested next: one upfront consent for automatic download plus upgrade (manual
  source confirmation retained), Windows 10 target upgrades and equal-version
  repair using target-aware Setup arguments, and application self-update.
- [Next iteration plan](NEXT-ITERATION-PLAN.md) records source-backed behavior,
  acceptance cases and proposed PR order. The owner subsequently approved staged
  execution; PR #8 is now merged. Design-only PR #9 remains separate and does
  not implement its proposed runtime feature. Each checkpoint needs its own CI.

### Validation repetition and mount diagnostics (investigation only)

- The latest supplied test log records ISO hashing at 09:18:46, then failure at
  09:19:23 because the mount helper did not discover a volume drive letter within
  its 30-second poll. It does not record completed DISM image validation or a
  Windows Setup launch. This does not establish corruption or prove that Windows
  itself had no mounted volume; the helper may have failed to discover one.
- After Start at 09:19:53, the same SHA-256 was recorded again at 09:23:09. The
  first snapshot ended there. The extended log now confirms the second attempt
  failed with the same drive-letter timeout at 09:23:44. A hash without an
  independent trusted expected value is not full media validation.
- The screenshot shows `#< CLIXML` in the status line. Source review confirms
  that the helper throws raw PowerShell stderr on nonzero exit, then the GUI
  displays that raw exception message. The serialized output includes 96 progress
  records and the real drive-letter timeout error. This is not merely harmless
  progress misclassified as failure: an actual error is present.
- Source review confirms that Inspect and Start both hash a selected ISO and
  enumerate its images; StartUpgradeAsync repeats source/image validation before
  launch. Prepared state currently preserves selection, not a reusable validation
  result that avoids this work.
- Pending: separate a concise, actionable mount error from full diagnostic detail;
  suppress/parse serialized progress without hiding actual errors; improve logging
  of attached-image/volume discovery and the exact failed stage.
- Pending: design safe reuse of successful expensive validation within a session.
  Define file identity, mount/source binding and invalidation rules; file size and
  timestamps alone do not prove unchanged content. Never cache a failed attempt
  as success or remove final attached-answer-file/storage/source/index safeguards.
- Suggested acceptance cases: Validate then Start on unchanged media avoids
  redundant full hashing; changed/replaced/ejected media invalidates the result;
  a newly attached unsafe answer file still blocks; a drive-letter timeout gives
  a readable message rather than XML; both GUI and CLI follow the same rules.
- No application fix or replacement executable was produced during this report.
  The owner remains in field testing. Findings are recorded for a later authorized
  change; do not silently replace the test artifact being evaluated.

### Reported ISO eject failure requiring VM restart (unresolved)

- The owner reports the ISO remained mounted and could not be ejected until a VM
  restart. The extended log shows the two mount-discovery timeouts above, followed
  by selected-source access denied for an answer file on volume F at 09:24:47 and
  another answer-file block at 09:24:51. No Setup launch is recorded in that early
  segment; the later folder-source launch below is a separate attempt.
- The log does not map the failed helper's backing ISO path to volume F, record
  an eject attempt/result, identify a locking process, or show whether the app
  was closed or running from that volume. Do not claim an application handle leak
  or Windows Storage service fault as the proven cause.
- Access denied while deleting an answer file on read-only ISO media is not
  evidence that another process locked the volume. A mount remaining attached is
  likewise different from a demonstrated eject-blocking open handle.
- Source review finds no dismount/ownership cleanup after mounting/validation
  fails. The PowerShell helper is awaited and disposed on normal nonzero exit;
  that code path alone does not prove a helper process was left running.
- Pending: track exact ISO/device/volume identity, pre-existing versus app-created
  mounts, active consumers and operation stage; log bounded mount/eject results.
  Design safe cleanup/release of only app-created mounts after failure/cancel when
  unused. Never detach a user-preexisting mount on failure, interrupt Setup or
  force-close unrelated processes. Keep approved unsafe-media remediation scoped
  separately as proposed in PR #9.
- Regression cases: mount then failed validation, cancel after mount, repeated
  validation, pre-existing mount preserved, app launched from ISO versus local,
  and a real in-use mount. Distinguish clean release from a readable manual-action
  blocker; do not reboot the machine automatically to work around a locked image.
- The owner subsequently confirmed the ISO file was local and eject still failed
  after closing the assistant, requiring a VM restart. This does not establish
  where the EXE itself ran or identify a locking process. Still useful: executable
  location, exact eject error and live process/handle evidence before restarting.
  This remains an investigation, not a product-code fix.

### Windows Setup crash from a folder source (investigation only)

- A later attempt is corroborated by the assistant log and Last-Upgrade state:
  original build **19043** (Windows 10 21H1), target **22621** (Windows 11 22H2),
  target edition Professional, image index 1. The owner's recollection of a 21H2
  target is not what these recorded build numbers identify.
- Source was an extracted folder using `sources/install.swm`, not a mounted ISO
  for this launch. Signature and enumeration of three images passed. Enumeration
  repeated at 09:32:10, 09:32:21 and 09:32:23; Setup launched at 09:32:24 and
  returned at 09:33:09 (UTC+03:00). The repeated-enumeration finding is therefore
  visible independently of the earlier mount failure.
- The launched command used the existing upgrade-only switches plus image index 1
  and InstallFrom bound to the first SWM. The process exit was **-1073741819**, or
  **0xC0000005 (STATUS_ACCESS_VIOLATION)**. This indicates an invalid memory access
  termination, not a filesystem Access Denied result. The faulting component is
  not identified by the assistant log; do not blame a particular module or ISO.
- Stored state OutcomeUnconfirmed is not proof of success or completed migration.
  No forced reboot was issued. Do not infer Windows Setup never started from the
  earlier mount errors: this later attempt did start its process.
- SWM is supported by the documented InstallFrom option; the extension alone is
  not a reason to declare this command invalid. Still verify the complete split
  set and media consistency; metadata enumeration does not validate every part.
- Needed before a diagnostic retry: capture the test machine's
  `$WINDOWS.~BT/Sources/Panther/setupact.log` and `setuperr.log` from its Windows
  volume, plus Application Error / Windows Error Reporting details around
  09:33 (faulting application/module, exception code and offset). If staging logs
  are absent, inspect Windows/Panther and the crash event; absence is not success.
- Also request the SWM part names/sizes and Setup file version, without publishing
  raw logs or customer data. No argument change, ISO deletion, clean-install
  fallback or actual Setup execution is authorized by this investigation.
- References: [Windows 10 build mapping](https://learn.microsoft.com/en-us/windows/release-health/release-information),
  [Windows 11 build mapping](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information),
  [Access violation](https://learn.microsoft.com/en-us/shows/inside/c0000005),
  [InstallFrom](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/windows-setup-command-line-options#installfrom).

### Panther follow-up: crash narrowed to compatibility appraisal

- The owner supplied Setup's setupact/setuperr logs from the same failed attempt;
  they end at 09:33:08, immediately before the assistant's recorded process exit.
  The test VM is already undergoing another upgrade; do not request interruption
  or run diagnostic repair/cleanup while that upgrade is active.
- SetupHost identifies media Setup build 10.0.22621.1, host Professional x64 build
  19043, host/default media UI language en-US, the selected folder/SWM source and
  the requested automatic-upgrade command. This is not a logged language mismatch
  or a failure to launch Setup.
- At 09:33:08, CONX/Appraiser records ShimExceptionHandler exception 0xc0000005,
  repeated eight times with the same exception address. Immediately beforehand,
  application inventory reports completion (98 apps / 324 files), then the
  appraisal path crashes. Repeated lines are not proof of eight upgrade attempts.
  The reported module name is empty, so the precise faulting binary/stack remains
  unknown. This narrows the failing subsystem, not the final root cause.
- Earlier SdbpGetMergeSdbsDisabled [c0000034] messages are investigation clues,
  not independently demonstrated causes: the initial requirements appraisal
  subsequently completes with NoIssue. Similarly, the last driver-coverage line
  before an exception does not establish that driver coverage caused the crash.
- The supplied logs do not show a CRC/checksum failure, image decompression error,
  or a confirmed corrupt SWM part. They do not prove that media is intact either.
  File-not-found inventory messages for installed apps and early optional-footprint
  warnings must not be presented as proof of a damaged ISO without correlation.
- The existing signature/DISM/hash checks are distinct from Windows' later
  application/driver compatibility appraisal. Preserve both safety layers; do not
  remove Appraiser files, reinterpret the crash as success, or use clean-install
  fallback to get past it. No product code/arguments were changed.
- Future controlled comparison: reuse a VM snapshot with the exact same source
  system and a verified complete media set, changing one factor at a time.
  Capture compatibility XML and a crash event/dump if it recurs. Success with a
  different target Setup build alone cannot prove corruption of the old ISO.
- Dynamic Update was disabled in the recorded command. Microsoft documents that
  Dynamic Update can refresh Setup/compatibility data, but this does not prove
  that disabling it caused this crash. Any such experiment needs separate owner
  authorization and unsupported-hardware regression testing; do not flip the
  current default or silently modify the owner's ISO.
- Reference: [Compatibility scan logs](https://learn.microsoft.com/en-us/troubleshoot/windows-client/setup-upgrade-and-drivers/use-windows-setup-compatibility-scan-logs-to-identify-blocking-issues),
  [Setup Dynamic Update](https://learn.microsoft.com/en-us/windows/deployment/update/media-dynamic-update).

### Earlier findings

- Future safe-media preparation is recorded separately in
  [Draft PR #9](https://github.com/JavierTorresFelendler/pcninja-win11-upgrade-assistant/pull/9),
  branch `design/safe-media-preparation`, file
  `docs/design/SAFE-MEDIA-PREPARATION.md`. This is a design-only PR based on main;
  that file is not part of this branch yet. The proposed flow chooses a suitable
  local destination or lets the user choose manually, copies/validates into owned
  staging, removes known answer files from the copy only, relocates an assistant
  running from the ISO, then detaches the exact consented unsafe Windows ISO mounts.
  Unremovable USB/hypervisor/locked media remains a manual blocker. Final attached
  safety checks and explicit upgrade confirmation remain. No capability was
  implemented/tested by that design; do not replace the owner's PR #8 test EXE.

- The owner reported successful automatic ISO download on Windows 10 with RC5.
  Download success alone does not confirm the final upgrade from that file.
- A mounted original ISO passed signature/image enumeration, but Start was
  blocked by about 11 GB free on the Windows drive versus the tool's 20 GB gate.
  Pending UI improvement: show current/required/missing space prominently with a
  Refresh instruction; this storage-message improvement is not implemented here.
- A folder containing Windows 10 images was read successfully on Windows 11
  24H2. The first recorded rejection was a language mismatch. The old-target
  guard would also reject it. Pending improvement: aggregate rejection reasons
  instead of reporting only the first one.
- Multiple downloadable target OS versions were discussed, not implemented.
  Windows 10 targets, downgrade support, UI translations and catalog-driven
  multi-language automatic downloads are outside this change.
- Answer files on attached installation media remain a data-loss risk. Preserve
  the RC5 removal/blocking and final prelaunch checks; never add a bypass.

## Resume procedure

1. Read `AGENTS.md`, the AI maintenance playbook, this document, the current branch
   and its Draft PR.
2. Inspect current git status and CI; do not assume a previous test applies to a
   different commit or that a conversation summary is release authorization.
3. Keep owner-reported field evidence separate from automated/synthetic checks.
4. Implement only authorized work, then commit/push the checkpoint and update
   the same Draft PR. Merge and release are separate owner decisions.
