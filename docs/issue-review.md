# Similar issue review

This is a focused source-code review, not a full security audit or a Windows runtime test. Findings below are potential or confirmed code-path issues; priorities describe impact, not how frequently users will encounter them.

## Addressed in this change

- **Size sorting:** the grid displayed a formatted string and sorted that string. It now sorts by numeric `SizeBytes` while displaying the original formatted value. Type and status template columns also have explicit sort paths.
- **Community folder selection:** settings saved a 2024 override, but the view model always used the detected path. The active simulator's override now takes precedence, on startup, after saving settings, and when switching simulators. The Settings field edits the selected simulator's override; if that simulator has no override, the detected folder is used. With no detected simulator, the saved active simulator (2024 by default) is used.
- **Stale folder consumers:** the backup and profile panels held the path from their first initialization. They now receive path changes, and reject missing folders. Older in-flight scans are prevented from replacing a newer folder's results.
- **Invalid settings:** saving an empty or nonexistent custom folder is rejected before mutating shared settings. A saved path that later disappears is reported rather than silently replaced with the detected folder.

Changing the destination does not move existing mods. A previous release stored the Settings field in the 2024 slot regardless of the selected sim; 2020 users should reselect their custom folder while MSFS 2020 is selected.

## Further issues to investigate

| Priority | Finding | Suggested follow-up |
| --- | --- | --- |
| High | `ModInstallerService.InstallSingleModAsync` deletes an existing Community destination and library folder before copying a replacement. An interrupted or failed copy can lose the previous version. | Stage and validate the replacement, then swap with rollback and tests. |
| High | `UninstallMod` deletes the resolved target of a junction without checking whether it is owned by this app's library. An externally managed target may be removed. | Validate ownership and default to removing only the link for external targets. |
| High | Disabled mods are stored in one global `DisabledModsDirectory`; scanning labels all of them with the current simulator, regardless of origin. Switching simulators or custom folders can expose the same disabled mods to the other setup. | Design per-installation storage and a safe migration for existing disabled mods. |
| Medium | `SecurityScanner.ScanDirectory` exists but has no call sites, despite the README saying installation checks dangerous files. | Wire a review/warning step into installation and clarify that it is not antivirus; test archive extraction safety separately. |
| Medium | Batch uninstall increments its success counter even when `MainViewModel.UninstallMod` catches a failure internally. | Return an explicit result and report failed items accurately. |
| Medium | Watch-folder service starts at launch but Settings save does not restart it when the watch path or enabled state changes. | Apply changed watcher settings immediately and report errors. |
| Low | `AnalyticsPanel.Refresh` returns immediately for an empty mod list, leaving old numbers on screen after switching to an empty folder. | Clear analytics when the list becomes empty. |

## Verification limits

See `tests/ModInstaller.Regression/README.md` for automated checks and the Windows manual checklist. Code review alone cannot verify WPF interactions, simulator detection, real junctions, or archive installation.

The mod database also indexes mods by folder name globally rather than by simulator or Community folder; profiles and notes likewise use folder names. This deserves a multi-simulator collision audit before changing the data model. In addition, folder switching during an in-progress install or profile operation should be tested and, if needed, serialized or disabled until the operation completes.
