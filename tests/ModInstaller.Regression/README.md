# Regression checks

On Windows with the .NET 8 SDK, run from the repository root:

```powershell
dotnet run --project tests/ModInstaller.Regression
```

The standalone checks exercise sorting, per-simulator folder selection, scoped disabled storage, ZIP validation, installation, restoration, staged replacement, rollback, interrupted recovery and junction handling. They use temporary folders, not real mod folders. They are not a full security audit or substitute for testing the application and simulator.

Also verify these Windows UI scenarios using disposable test folders and test mods (not a real Community folder):

1. Sort mods of different sizes in B, KB, MB and GB both ways. Check repeated size values, type and status sorts.
2. Select MSFS 2020, set an existing custom folder in Settings, then install via the button and drag-and-drop. Verify scanning, Open Community Folder, enable, profile loading, backup and restore use that folder. Repeat with MSFS 2024 and separate paths.
3. Open backup/profile pages before changing folders, switch simulators and change custom settings; verify both pages use the current folder, not their initial folder.
4. Restart and verify the saved override is used. Disable the override and verify the selected simulator's detected folder is used. With no detected simulator, set a custom path and restart.
5. Make the configured custom folder unavailable, then restart: confirm an error is shown and nothing is installed to the detected folder. Restore the path and refresh.
6. Try saving an empty or missing custom folder, cancel after the validation message, and check settings have not changed. Switch rapidly between large folders while scans are in progress and confirm only the current folder's results appear.

7. In two disposable Community folders, install same-named packages, disable each, switch between installations and confirm files stay separate. Test the legacy disabled-data assignment prompt, including collisions and a failed migration. Keep copies of all original folders.
8. Install a replacement over a copy and over an externally managed junction. Confirm the old item remains in the `.msfsmm-recovery-*` folder and that its junction target is untouched. Uninstall the replacement and confirm it does not resurrect the old item.
9. Restore a single directory backup, a ZIP backup and a full backup with and without replacement. Confirm the warning, retained old content and behavior after a failure midway through a full restore. It is not all-or-nothing.
10. On test data, interrupt replacement during staging and promotion. Check that old files are retained and ambiguous interruptions are reported for manual inspection. Do not delete the recovery folder until you have verified the contents and their targets.
11. Test nested and dangling links, cross-volume locations, inaccessible files and paths with unusual characters. Confirm external targets are never removed. Test external archive formats only with trusted archives; their security has not been fully reviewed.
12. Create same-named profiles and notes in both Community folders. Confirm they remain separate, explicitly import legacy items without overwriting new ones, restart and verify persistence. Switch folders during profile loading; check any partial changes are reported.
13. Run a large-library scan, conflict check, and simultaneous operations. Check the UI remains responsive and no results are lost. Finally confirm loading the resulting mods in each simulator; the automated checks do not launch MSFS.
