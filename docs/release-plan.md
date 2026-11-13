# Beta release plan

The beta must remain a prerelease, not replace the latest stable release. Do not publish an executable until Windows build and regression checks pass. Run the manual checklist in `tests/ModInstaller.Regression/README.md` with disposable data, including junction, multi-simulator and interrupted-operation scenarios. Keep independent backups; the beta is not yet a substitute for the stable release.

## Phase 1 — Data safety

- [x] Reject install replacement and enable/disable collisions rather than deleting existing data. Replacement/restore overwrite is temporarily unavailable pending a transactional implementation.
- [x] Stage new copies and never delete a junction target during uninstall. Resolve disabled junction markers for enable and backup.
- [x] Restore full backups to individual folders without overwriting; restrict automatic cleanup to metadata-marked directories.
- [x] Avoid overwriting existing V3 profiles during V2 import and avoid repeating the import.
- [ ] Test recovery, unusual reparse points, copy failures, full backup and marker migration on Windows; add a truly transactional replacement/restore with rollback.
- [ ] Isolate disabled mods and identity by installation with an explicit migration for old data. Existing disabled storage is still shared.

## Phase 2 — Security and correctness

- [x] Use boundary-aware ZIP path containment, stop falling back after a detected traversal, remove the PowerShell extraction fallback, and drain extractor output asynchronously.
- [x] Fix size sort and custom Community folder selection; propagate the selected path to profile/backup panels.
- [x] Preserve mod change notifications, truthful batch uninstall/toggle results, window position, and empty analytics.
- [ ] Review all extractor formats and reparse points, archive limits, package-root/multi-package detection, security scan integration, and simulator detection.
- [ ] Fix operation concurrency, conflict-scan completeness, per-mod scan errors, watch-folder changes and advertised-but-unwired features.

## Phase 3 — Scale and release

- [ ] Implement incremental/background scanning and bounded conflict indexing; synchronize database access; validate large libraries.
- [ ] Complete Windows automated and manual tests; ensure release notes honestly list remaining limitations.
- [ ] Publish a tested single-file Windows artifact and checksums as a GitHub prerelease, without marking it latest.
