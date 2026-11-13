# Beta release plan

The beta must remain a prerelease, not replace the latest stable release. Do not publish an executable until Windows build and regression checks pass. Run the manual checklist in `tests/ModInstaller.Regression/README.md` with disposable data, including junction, multi-simulator and interrupted-operation scenarios. Keep independent backups; the beta is not yet a substitute for the stable release.

## Phase 1 — Data safety

- [x] Require confirmation for install replacement; enable/disable collisions are rejected. Replaced items are retained for recovery.
- [x] Stage new copies and never delete a junction target during uninstall. Resolve disabled junction markers for enable and backup.
- [x] Restore full backups to individual folders, with optional staged replacement; restrict automatic cleanup to directories with a current-format manager marker; retain older backups.
- [x] Avoid overwriting existing V3 profiles during V2 import and avoid repeating the import.
- [ ] Test recovery, unusual reparse points, copy failures, full backup and marker migration on Windows. Replacement rolls back a failed promotion where possible, but batch operations are not transactional and ambiguous interruptions require manual review.
- [x] Isolate disabled mods and new library content by Community folder; ask before assigning old shared disabled data.
- [x] Scope new profiles to a Community folder; keep legacy profiles unassigned and allow explicit non-destructive copies.
- [x] Scope new mod database records by Community folder and preserve the old globally keyed records without guessing ownership.
- [x] Scope new notes by Community folder and offer explicit, non-overwriting copies of legacy notes.
- [ ] Review legacy mod metadata migration and alias paths.

## Phase 2 — Security and correctness

- [x] Use boundary-aware ZIP path containment, stop falling back after a detected traversal, remove the PowerShell extraction fallback, and drain extractor output asynchronously.
- [x] Fix size sort and custom Community folder selection; propagate the selected path to profile/backup panels.
- [x] Preserve mod change notifications, truthful batch uninstall/toggle results, window position, and empty analytics.
- [x] Validate ZIP boundaries, links, size and entry limits; support detected multi-package and root-level ZIPs and ZIP backup restoration.
- [ ] Fully harden 7z/RAR extraction before it writes any files, integrate appropriate security scanning, and review simulator detection. The UI warns before external extraction, and nested links are rejected after extraction and before copying; this is not a sandbox.
- [x] Apply watch-folder changes when Settings is saved.
- [ ] Fix operation concurrency and advertised-but-unwired features. Scanning reports per-mod read failures; conflict scans report unreadable paths and nested links, invalidate stale results, and stop rather than silently truncate when limits are exceeded.

## Phase 3 — Scale and release

- [ ] Implement incremental mod scanning; synchronize database access; validate large libraries. Community and conflict scanning run in the background; conflict indexing has explicit limits and does not write through the shared SQLite connection.
- [ ] Complete Windows automated and manual tests; ensure release notes honestly list remaining limitations.
- [ ] Publish a tested single-file Windows artifact and checksums as a GitHub prerelease, without marking it latest.

## Verification status

Windows build, regression checks and single-file publish passed in GitHub Actions on the working branch. The manual checklist and outstanding safety/security work remain open; the passing checks are not full simulator validation.

## Audit item tracking

Numbers match the 37-item audit in the conversation. "Partial" means mitigation or limited coverage, not a completed fix.

| Audit item(s) | Phase | State / follow-up |
| --- | --- | --- |
| 1–5: size, custom path, stale panels/scans and settings validation | 2 | Implemented; manual simulator tests pending. |
| 6: destructive replacement | 1 | Partial: replacement asks for confirmation, stages content and retains the old entry; interruption and multi-package recovery need broader tests. |
| 7: external junction targets | 1 | Partial: uninstall preserves all link targets; add ownership tracking and safe cleanup. |
| 8–10: disabled junction markers, collisions and backups | 1 | Partial: marker targets are respected, collisions rejected, target backed up; test failure and legacy cases, and add ownership-aware cleanup. |
| 11–13: full/ZIP backups and restore safety | 1 | Partial: ZIP and full restore support staged replacement with old entries retained; full operations can partially complete. |
| 14: unsafe cleanup | 1 | Partial: cleanup requires a current-format manager marker and skips links, legacy backups and ZIPs; marker ownership is not cryptographically authenticated. |
| 15: repeated V2 profile import | 1 | Existing names are preserved and migration is recorded; test migration failures and legacy formats. |
| 16–17: shared disabled storage and global identities | 1 | Partial: disabled and new library files are scoped with explicit legacy import; new profiles are scoped and legacy profiles require explicit assignment; new mod metadata and notes are scoped; legacy records remain unassigned, with explicit non-overwriting note import. |
| 18–21: extraction path validation, scanning, process and command safety | 2 | Partial: ZIP path boundary and cancellation fixed, unsafe PowerShell fallback removed and pipes drained; harden all fallback formats, limits and scanning. |
| 22–23: archive root naming and multi-package archives | 2 | Implemented for detected packages; partial failure and unusual archives need further work. |
| 24–25: detection and multi-install custom settings | 2 | Partial: corrected Steam IDs and prioritize simulator configuration; custom settings are per simulator, not per installation. Manual detection tests remain. |
| 26: concurrent operations | 2 | Partial: core file mutations are gated in-process and profile loads stop on folder changes; scan overlap, settings changes, external processes and cross-operation batches need further work. |
| 27–28: notifications and operation results | 2 | Partial: core status properties and toggle/batch uninstall reporting improved; audit other actions. |
| 29–31: conflict and scan reliability | 2 | Partial: conflicts use enabled mods, report incomplete scans, avoid SQLite worker writes, and invalidate stale results; unreadable individual mods are reported during Community scans. |
| 32–33: incomplete advertised features and settings | 2 | Partial: nonfunctional startup update/tray controls are disabled and README claims corrected; audit the remaining controls and localization. |
| 34: watch settings | 2 | Implemented; Windows UI manual test pending. |
| 35–36: window placement and stale analytics | 2 | Implemented; Windows UI manual test pending. |
| 37: suppressed errors | 2 | Open: structured errors, UI reporting and recovery. |
| Large libraries, repeated scanning, conflicts and DB concurrency | 3 | Open: benchmark and build incremental indexing, bounded work and safe database concurrency. |
