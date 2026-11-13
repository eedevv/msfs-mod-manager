# Changelog

## Unreleased beta (work in progress)

This beta is not ready to publish until the Windows release checklist passes. Test with disposable folders and keep independent backups. It does not yet resolve all issues in the audit.

### Changes so far
- Size sorting uses numeric bytes; custom Community folders are applied for the selected simulator and propagated to backup and profile pages. Simulator detection prioritizes configured package paths and uses corrected Steam app IDs.
- Install replacements require confirmation; staged replacements retain the displaced folder or link in a `.msfsmm-recovery-*` folder. Failed promotions roll back where possible.
- Uninstall preserves link targets and resolves disabled-link markers for backups; disabled links are restored to their original target. Directory deletion avoids traversing nested reparse points.
- Full and ZIP backups can be restored; package names are checked before restore or install and optional replacement keeps the displaced folder or link. The mod list refreshes after restore, including partial failure. Automatic cleanup only removes directories with an explicit current-format manager marker; older backups and ZIPs are retained.
- ZIP archives are validated before extraction, with traversal, Windows path, duplicate-destination and link checks and uncompressed-size and entry-count limits; actual decompressed bytes are also checked. ZIP extraction no longer falls back to external extractors.
- Extractor output is drained asynchronously, and V2 profile import no longer overwrites existing profiles or repeats after successful migration.
- Improved status change notifications, uninstall/toggle reporting, window position restoration and empty analytics. Conflict scans now report incomplete scans, invalidate stale results and impose safety limits instead of silently claiming a clean scan.
- Apply watch-folder settings immediately, scan Community folders in the background, report individual unreadable mods and avoid listing temporary staging folders as mods.
- Reject overlapping core file mutations within the running application rather than running them simultaneously; operations from other processes are not coordinated.
- Disabled mods and newly managed library content are separated by Community folder. Legacy disabled mods require explicit assignment; collisions are left untouched.
- New mod database records, profiles and notes are scoped to their Community folder. Old global records are retained but not automatically assigned; legacy profiles and notes can be copied explicitly without overwriting scoped data.
- Archives containing multiple detected packages install each package, and root-level ZIPs use a stable package folder name.

### Known limitations
- Restore retains `.backup_meta.json` files to avoid deleting user data. A single-folder backup refuses a mod that already contains that filename; use a full or ZIP backup instead.
- Replaced folders and links are retained in `.msfsmm-recovery-*` folders inside Community and need manual cleanup. Uninstall also preserves link targets; library folders may require manual cleanup.
- Legacy disabled mods cannot be assigned to the right installation automatically. Review the migration prompt carefully and keep an independent backup; legacy global mod records remain unassigned. Legacy profiles and notes require explicit assignment to a Community folder before use; alias paths to the same physical folder may be treated as different installations.
- Full restore and multi-package installs can be partially completed if a later mod fails. If a replacement is interrupted and its destination is missing, the original is retained in the recovery folder for manual review rather than automatically restored.
- Junction creation rejects paths containing `%` to prevent command expansion; use copy mode for those paths. Existing links can still be removed without deleting their targets.
- 7z/RAR extraction is performed by external 7-Zip before inspection and is not subject to ZIP path or extraction size safeguards; the app warns before extraction. Use only trusted archives. Nested links within copied content are rejected, which may require restructuring some mods. These checks do not replace antivirus or a security audit.
- The remaining audit and scaling items are tracked in `docs/release-plan.md`.
