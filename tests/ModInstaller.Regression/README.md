# Regression checks

On Windows with the .NET 8 SDK, run from the repository root:

```powershell
dotnet run --project tests/ModInstaller.Regression
```

The standalone checks exercise the real WPF collection view's size sorting, the XAML sort binding, per-simulator folder resolution, persistence, switching, and unavailable paths. They do not touch real mod folders.

Also verify these Windows UI scenarios using disposable test folders and test mods (not a real Community folder):

1. Sort mods of different sizes in B, KB, MB and GB both ways. Check repeated size values, type and status sorts.
2. Select MSFS 2020, set an existing custom folder in Settings, then install via the button and drag-and-drop. Verify scanning, Open Community Folder, enable, profile loading, backup and restore use that folder. Repeat with MSFS 2024 and separate paths.
3. Open backup/profile pages before changing folders, switch simulators and change custom settings; verify both pages use the current folder, not their initial folder.
4. Restart and verify the saved override is used. Disable the override and verify the selected simulator's detected folder is used. With no detected simulator, set a custom path and restart.
5. Make the configured custom folder unavailable, then restart: confirm an error is shown and nothing is installed to the detected folder. Restore the path and refresh.
6. Try saving an empty or missing custom folder, cancel after the validation message, and check settings have not changed. Switch rapidly between large folders while scans are in progress and confirm only the current folder's results appear.
