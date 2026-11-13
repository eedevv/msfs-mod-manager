# Changelog

## Unreleased beta (work in progress)

This beta is not ready to publish until the Windows release checklist passes. Test with disposable folders and keep independent backups. It does not yet resolve all issues in the audit.

### Changes so far
- Size sorting uses numeric bytes; custom Community folders are applied for the selected simulator and propagated to backup and profile pages.
- Install and enable/disable collisions are rejected instead of overwriting existing files. New install copies are staged.
- Uninstall preserves link targets and resolves disabled-link markers for backups; disabled links are restored to their original target.
- Full backups restore each contained mod without overwriting; automatic cleanup skips unmarked and ZIP backups.
- ZIP path containment is boundary-aware; unsafe traversal and cancellation do not fall through to other ZIP extractors. PowerShell extraction fallback was removed.
- Extractor output is drained asynchronously, and V2 profile import no longer overwrites existing profiles or repeats after successful migration.
- Improved status change notifications, uninstall/toggle reporting, window position restoration, empty analytics and conflict wording.

### Known limitations
- Existing-mod replacement and restore-overwrite are temporarily disabled; move or back up the existing destination manually first. Uninstall preserves link targets, so library folders may require manual cleanup.
- Disabled-mod storage is still shared across installations. Do not switch simulators with disabled mods without an independent backup.
- ZIP backup restore is not yet supported. Full restore can be partially completed if a later mod fails.
- Extraction and junction changes require Windows validation; untrusted archives should be treated with care.
- The remaining audit and scaling items are tracked in `docs/release-plan.md`.
