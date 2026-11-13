# Changelog

## Unreleased beta (work in progress)

This beta is not ready to publish until the Windows release checklist passes. Test with disposable folders and keep independent backups. It does not yet resolve all issues in the audit.

### Changes so far
- Size sorting uses numeric bytes; custom Community folders are applied for the selected simulator and propagated to backup and profile pages.
- Install replacements require confirmation; staged replacements retain the displaced folder or link in a `.msfsmm-recovery-*` folder. Failed promotions roll back where possible.
- Uninstall preserves link targets and resolves disabled-link markers for backups; disabled links are restored to their original target.
- Full and ZIP backups can be restored; optional replacement keeps the displaced folder or link. Automatic cleanup skips unmarked and ZIP backups.
- ZIP archives are validated before extraction, with traversal and link checks and uncompressed-size and entry-count limits. ZIP extraction no longer falls back to external extractors.
- Extractor output is drained asynchronously, and V2 profile import no longer overwrites existing profiles or repeats after successful migration.
- Improved status change notifications, uninstall/toggle reporting, window position restoration, empty analytics and conflict wording.
- Apply watch-folder settings immediately and avoid listing temporary staging folders as mods.
- Disabled mods and newly managed library content are separated by Community folder. Legacy disabled mods require explicit assignment; collisions are left untouched.
- Archives containing multiple detected packages install each package, and root-level ZIPs use a stable package folder name.

### Known limitations
- Replaced folders and links are retained in `.msfsmm-recovery-*` folders inside Community and need manual cleanup. Uninstall also preserves link targets; library folders may require manual cleanup.
- Legacy disabled mods cannot be assigned to the right installation automatically. Review the migration prompt carefully and keep an independent backup; global database identity and profile isolation remain unfinished.
- Full restore and multi-package installs can be partially completed if a later mod fails. Recovery and interruption handling need further testing.
- Extraction and junction changes require Windows validation; untrusted archives should be treated with care.
- The remaining audit and scaling items are tracked in `docs/release-plan.md`.
