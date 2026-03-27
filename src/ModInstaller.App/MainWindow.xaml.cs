using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ModInstaller.App.ViewModels;
using ModInstaller.App.Views;
using ModInstaller.Core.Models;
using ModInstaller.Core.Services;

namespace ModInstaller.App;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private readonly HashSet<string> _initializedPages = ["Mods"];

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        // Apply localization on load and when language changes
        Loaded += (_, _) => ApplyLocalization();
        LocalizationService.LanguageChanged += () => Dispatcher.Invoke(ApplyLocalization);

        // Keyboard shortcuts
        InputBindings.Add(new KeyBinding(new RelayShortcut(() => ViewModel.CurrentPage = "Mods"), Key.D1, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayShortcut(() => { ViewModel.CurrentPage = "Backups"; LazyInitPage("Backups"); }), Key.D2, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayShortcut(() => { ViewModel.CurrentPage = "Profiles"; LazyInitPage("Profiles"); }), Key.D3, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayShortcut(() => { ViewModel.CurrentPage = "Conflicts"; LazyInitPage("Conflicts"); }), Key.D4, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayShortcut(() => { ViewModel.CurrentPage = "Health"; LazyInitPage("Health"); }), Key.D5, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayShortcut(() => { ViewModel.CurrentPage = "Analytics"; LazyInitPage("Analytics"); }), Key.D6, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayShortcut(async () => { try { await ViewModel.RefreshModListAsync(); } catch { } }), Key.R, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayShortcut(async () =>
        {
            try
            {
                await ViewModel.ExportModListAsync();
                ShowToast("Exported", "Mod list saved to Desktop", ToastNotification.ToastType.Success);
            }
            catch { }
        }), Key.E, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayShortcut(() => InstallButton_Click(this, new RoutedEventArgs())), Key.I, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayShortcut(() => ModGrid.SelectAll()), Key.A, ModifierKeys.Control));
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var startupTimer = System.Diagnostics.Stopwatch.StartNew();

        RestoreWindowPosition();
        await ViewModel.InitializeAsync();

        startupTimer.Stop();

        // V2 migration
        var migrator = new V2Migrator();
        if (migrator.HasV2Data && ViewModel.Database != null)
        {
            int profiles = migrator.ImportProfiles(ViewModel.Database);
            if (profiles > 0)
                ShowToast("V2 Import", $"Imported {profiles} profile(s) from V2", ToastNotification.ToastType.Info);
        }

        // Show walkthrough on first launch
        if (!ViewModel.Settings.HasCompletedTutorial)
        {
            Walkthrough.Show(() =>
            {
                try
                {
                    ViewModel.Settings.HasCompletedTutorial = true;
                    ViewModel.SaveSettingsSync();
                    ShowToast("Welcome!", "You're all set. Enjoy managing your mods!", ToastNotification.ToastType.Success);
                }
                catch { /* Settings save failure is non-critical */ }
            });
        }
        else
        {
            // Show ready toast (don't show during tutorial)
            if (ViewModel.DetectedInstallations.Count > 0)
                ShowToast("Ready", $"{ViewModel.TotalModCount} mods loaded in {startupTimer.ElapsedMilliseconds}ms", ToastNotification.ToastType.Info);
            else
                ShowToast("No MSFS Found", "Set a custom path in Settings", ToastNotification.ToastType.Warning);

            // Show donate reminder on 3rd launch, then every 10th
            var launches = ViewModel.Settings.LaunchCount;
            if (launches == 3 || (launches > 3 && launches % 10 == 0))
            {
                // Delay so it doesn't overlap the ready toast
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    ShowToast("Enjoying Mod Manager?",
                        "Consider supporting development on Patreon!",
                        ToastNotification.ToastType.Info);
                };
                timer.Start();
            }
        }

        // Track launch count
        ViewModel.Settings.LaunchCount++;
        ViewModel.SaveSettingsSync();

        // Initialize mod groups
        InitializeModGroups();

        // Start watch folder if enabled
        if (ViewModel.Settings.WatchFolderEnabled)
            StartWatchFolder();
    }

    // ── Mod Groups ──

    private ModGroupService? _groupService;

    private async void InitializeModGroups()
    {
        _groupService = new ModGroupService();
        try
        {
            await _groupService.LoadAsync(AppSettings.GroupsFilePath);
        }
        catch { /* First run — no groups file yet */ }
        PopulateGroupsMenu();
    }

    private void PopulateGroupsMenu()
    {
        if (_groupService == null) return;

        GroupsMenuItem.Items.Clear();
        foreach (var group in _groupService.GetGroups())
        {
            var item = new MenuItem { Header = group.Key };
            item.Click += async (_, _) =>
            {
                if (ViewModel.SelectedMod == null) return;
                _groupService.AddModToGroup(group.Key, ViewModel.SelectedMod.FolderName);
                try { await _groupService.SaveAsync(AppSettings.GroupsFilePath); } catch { }
                ShowToast("Group", $"Added to {group.Key}", ToastNotification.ToastType.Success);
            };
            GroupsMenuItem.Items.Add(item);
        }

        GroupsMenuItem.Items.Add(new Separator { Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(21, 255, 255, 255)) });

        var newGroupItem = new MenuItem { Header = "Create New Group..." };
        newGroupItem.Click += async (_, _) =>
        {
            var name = PromptForText("New Group", "Enter group name:");
            if (name != null)
            {
                _groupService!.CreateGroup(name);
                try { await _groupService.SaveAsync(AppSettings.GroupsFilePath); } catch { }
                PopulateGroupsMenu();
                ShowToast("Group Created", name, ToastNotification.ToastType.Success);
            }
        };
        GroupsMenuItem.Items.Add(newGroupItem);
    }

    private string? PromptForText(string title, string prompt)
    {
        var dialog = new Window
        {
            Title = title, Width = 360, Height = 160, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(19, 24, 32)),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 237, 243))
        };
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
        var textBox = new TextBox
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 32, 48)),
            Foreground = dialog.Foreground, Padding = new Thickness(10, 8, 10, 8), FontSize = 13,
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255))
        };
        stack.Children.Add(textBox);
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var okBtn = new Button { Content = "OK", Width = 70, Padding = new Thickness(0, 6, 0, 6), IsDefault = true };
        okBtn.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        btnPanel.Children.Add(okBtn);
        stack.Children.Add(btnPanel);
        dialog.Content = stack;
        textBox.Focus();
        return dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(textBox.Text) ? textBox.Text.Trim() : null;
    }

    // ── Watch Folder ──

    private WatchFolderService? _watchService;

    private void StartWatchFolder()
    {
        try
        {
            _watchService = new WatchFolderService();
            _watchService.NewModDetected += (filePath) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ShowToast("New Mod Detected", System.IO.Path.GetFileName(filePath), ToastNotification.ToastType.Info);
                });
            };
            _watchService.Start(ViewModel.Settings.WatchFolderPath);
        }
        catch { /* Watch folder path invalid or inaccessible */ }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _watchService?.Stop();
        _watchService?.Dispose();
        SaveWindowPosition();
    }

    // ── Window Position ──

    private void RestoreWindowPosition()
    {
        var s = ViewModel.Settings;
        if (s.WindowLeft >= 0 && s.WindowTop >= 0)
        {
            Left = s.WindowLeft;
            Top = s.WindowTop;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        Width = s.WindowWidth;
        Height = s.WindowHeight;
        if (s.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private void SaveWindowPosition()
    {
        var s = ViewModel.Settings;
        if (WindowState == WindowState.Normal)
        {
            s.WindowLeft = Left;
            s.WindowTop = Top;
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }
        s.WindowMaximized = WindowState == WindowState.Maximized;
        ViewModel.SaveSettingsSync();
    }

    // ── Lazy Panel Init ──

    private void LazyInitPage(string page)
    {
        if (!_initializedPages.Add(page)) return; // Already initialized

        switch (page)
        {
            case "Backups":
                BackupPage.Initialize(ViewModel.Settings, ViewModel.CommunityFolderPath, ShowToast);
                break;
            case "Profiles":
                if (ViewModel.Database != null)
                {
                    ProfilePage.Initialize(ViewModel.Database, ViewModel.Mods, ShowToast);
                    if (ViewModel.Installer != null)
                        ProfilePage.SetInstaller(ViewModel.Installer, ViewModel.CommunityFolderPath,
                            () => ViewModel.RefreshModListAsync());
                }
                break;
            case "Conflicts":
                if (ViewModel.Database != null)
                    ConflictPage.Initialize(ViewModel.Database, ViewModel.Mods, ShowToast);
                break;
            case "Health":
                HealthPage.Initialize(ViewModel.Mods, ShowToast);
                break;
            case "Analytics":
                AnalyticsPage.Refresh(ViewModel.Mods);
                break;
        }
    }

    private void ShowToast(string title, string message, ToastNotification.ToastType type)
    {
        Toast.Show(title, message, type);
    }

    // ── Localization ──

    private void ApplyLocalization()
    {
        // Install button
        InstallButtonText.Text = LocalizationService.Get("install_button");

        // Empty state
        NoModsFoundText.Text = LocalizationService.Get("no_mods_found");
        DragDropHintText.Text = LocalizationService.Get("drag_drop_hint");

        // Detail sidebar
        SelectModText.Text = LocalizationService.Get("select_mod");

        // Search placeholder
        SearchBox.Tag = LocalizationService.Get("search_placeholder");

        // DataGrid column headers
        ColName.Header = LocalizationService.Get("name").ToUpperInvariant();
        ColType.Header = LocalizationService.Get("type").ToUpperInvariant();
        ColVersion.Header = LocalizationService.Get("version").ToUpperInvariant();
        ColSize.Header = LocalizationService.Get("size").ToUpperInvariant();
        ColStatus.Header = LocalizationService.Get("status").ToUpperInvariant();

        // Context menu
        CtxToggle.Header = LocalizationService.Get("enable") + " / " + LocalizationService.Get("disable");
        CtxOpenFolder.Header = LocalizationService.Get("open_folder");
        CtxAddNote.Header = LocalizationService.Get("add_note");
        CtxEnableAll.Header = LocalizationService.Get("enable_all");
        CtxDisableAll.Header = LocalizationService.Get("disable_all");
        CtxUninstall.Header = LocalizationService.Get("uninstall");

        // Nav tooltips
        NavMods.ToolTip = LocalizationService.Get("nav_mods");
        NavBackups.ToolTip = LocalizationService.Get("nav_backups");
        NavProfiles.ToolTip = LocalizationService.Get("nav_profiles");
        NavConflicts.ToolTip = LocalizationService.Get("nav_conflicts");
        NavHealth.ToolTip = LocalizationService.Get("nav_health");
        NavAnalytics.ToolTip = LocalizationService.Get("nav_analytics");
        NavSettings.ToolTip = LocalizationService.Get("nav_settings");
        NavPatreon.ToolTip = LocalizationService.Get("about_support");

        // Sidebar action buttons
        ToggleButtonText.Text = LocalizationService.Get("toggle_enable_disable");
        OpenFolderButtonText.Text = LocalizationService.Get("open_folder");
        UninstallButtonText.Text = LocalizationService.Get("uninstall_mod");
    }

    // ── Navigation ──

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string page)
        {
            ViewModel.CurrentPage = page;
            LazyInitPage(page);

            // Refresh data when navigating
            switch (page)
            {
                case "Backups": BackupPage.RefreshList(); break;
                case "Profiles": ProfilePage.RefreshList(); break;
                case "Analytics": AnalyticsPage.Refresh(ViewModel.Mods); break;
            }
        }
    }

    // ── Double-click toggle ──

    private async void ModGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedMod != null)
        {
            await ViewModel.ToggleModAsync(ViewModel.SelectedMod);
            var action = ViewModel.SelectedMod.Status == ModStatus.Enabled ? "Enabled" : "Disabled";
            ShowToast(action, ViewModel.SelectedMod.DisplayName,
                ViewModel.SelectedMod.Status == ModStatus.Enabled
                    ? ToastNotification.ToastType.Success
                    : ToastNotification.ToastType.Warning);
        }
    }

    // ── Actions ──

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select mod to install",
            Filter = "Archives|*.zip;*.7z;*.rar|All files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            int ok = 0, fail = 0;
            foreach (var file in dialog.FileNames)
            {
                var before = ViewModel.TotalModCount;
                await ViewModel.InstallFromPathAsync(file);
                if (ViewModel.TotalModCount > before) ok++;
                else fail++;
            }

            if (fail > 0 && ok > 0)
                ShowToast("Partial Install", $"{ok} installed, {fail} failed", ToastNotification.ToastType.Warning);
            else if (fail > 0)
                ShowToast("Install Failed", "Could not install mod — check the status bar for details", ToastNotification.ToastType.Error);
            else
                ShowToast("Installed", $"{ok} mod(s) installed", ToastNotification.ToastType.Success);
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(ViewModel.Settings) { Owner = this };
        if (settingsWindow.ShowDialog() == true)
        {
            await ViewModel.SaveSettingsAsync();
            await ViewModel.RefreshModListAsync();
            ShowToast("Settings Saved", "", ToastNotification.ToastType.Success);
        }
    }

    private void Patreon_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.patreon.com/eedevv",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var mod = ViewModel.SelectedMod;
        if (mod != null)
        {
            try { using var p = Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = mod.FullPath, UseShellExecute = true }); }
            catch { }
        }
    }

    // ── Context Menu ──

    private async void ContextToggle_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedMod != null)
            await ViewModel.ToggleModAsync(ViewModel.SelectedMod);
    }

    private void ContextAddNote_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedMod == null) return;

        // Simple input dialog using WPF
        var dialog = new Window
        {
            Title = $"Note — {ViewModel.SelectedMod.DisplayName}",
            Width = 420, Height = 180, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(19, 24, 32)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(230, 237, 243))
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = "Add a note or tag:", Margin = new Thickness(0, 0, 0, 8) });
        var textBox = new TextBox
        {
            Text = ViewModel.SelectedMod.Note ?? "",
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(26, 32, 48)),
            Foreground = dialog.Foreground,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(40, 255, 255, 255)),
            Padding = new Thickness(10, 8, 10, 8), FontSize = 13
        };
        stack.Children.Add(textBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var saveBtn = new Button { Content = "Save", Width = 80, Padding = new Thickness(0, 6, 0, 6), IsDefault = true };
        saveBtn.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        var cancelBtn = new Button { Content = "Cancel", Width = 80, Padding = new Thickness(0, 6, 0, 6), Margin = new Thickness(8, 0, 0, 0) };
        cancelBtn.Click += (_, _) => dialog.Close();
        btnPanel.Children.Add(saveBtn);
        btnPanel.Children.Add(cancelBtn);
        stack.Children.Add(btnPanel);
        dialog.Content = stack;
        textBox.Focus();
        textBox.SelectAll();

        if (dialog.ShowDialog() == true)
        {
            var note = textBox.Text.Trim();
            ViewModel.SetModNote(ViewModel.SelectedMod, string.IsNullOrWhiteSpace(note) ? null : note);
            ShowToast("Note Saved", ViewModel.SelectedMod.DisplayName, ToastNotification.ToastType.Success);
        }
    }

    private async void ContextEnableAll_Click(object sender, RoutedEventArgs e)
    {
        var selected = ModGrid.SelectedItems.Cast<Mod>()
            .Where(m => m.Status == ModStatus.Disabled).ToList();
        if (selected.Count == 0) return;

        int count = 0;
        foreach (var mod in selected)
        {
            try
            {
                if (ViewModel.Installer != null)
                { await ViewModel.Installer.EnableModAsync(mod, ViewModel.CommunityFolderPath); count++; }
            }
            catch { }
        }
        await ViewModel.RefreshModListAsync();
        ShowToast("Batch Enable", $"Enabled {count} mod(s)", ToastNotification.ToastType.Success);
    }

    private async void ContextDisableAll_Click(object sender, RoutedEventArgs e)
    {
        var selected = ModGrid.SelectedItems.Cast<Mod>()
            .Where(m => m.Status == ModStatus.Enabled).ToList();
        if (selected.Count == 0) return;

        int count = 0;
        foreach (var mod in selected)
        {
            try
            {
                if (ViewModel.Installer != null)
                { await ViewModel.Installer.DisableModAsync(mod); count++; }
            }
            catch { }
        }
        await ViewModel.RefreshModListAsync();
        ShowToast("Batch Disable", $"Disabled {count} mod(s)", ToastNotification.ToastType.Warning);
    }

    private void ContextUninstall_Click(object sender, RoutedEventArgs e)
    {
        var selected = ModGrid.SelectedItems.Cast<Mod>().ToList();
        if (selected.Count == 0) return;

        var result = MessageBox.Show(
            $"Uninstall {selected.Count} mod(s)? Backups will be created.",
            "Confirm Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        int count = 0;
        foreach (var mod in selected)
        {
            try { ViewModel.UninstallMod(mod); count++; }
            catch { }
        }
        ShowToast("Uninstalled", $"Removed {count} mod(s)", ToastNotification.ToastType.Info);
    }

    // ── Drag & Drop ──

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        { e.Effects = DragDropEffects.Copy; DragOverlay.Visibility = Visibility.Visible; }
        else
        { e.Effects = DragDropEffects.None; }
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            int ok = 0, fail = 0;
            foreach (var file in files)
            {
                try { await ViewModel.InstallFromPathAsync(file); ok++; }
                catch { fail++; }
            }
            if (fail > 0)
                ShowToast("Partial Install", $"{ok} ok, {fail} failed", ToastNotification.ToastType.Warning);
            else
                ShowToast("Installed", $"{ok} mod(s) via drag & drop", ToastNotification.ToastType.Success);
        }
    }
}

internal class RelayShortcut : ICommand
{
    private readonly Action _execute;
    public RelayShortcut(Action execute) => _execute = execute;
    #pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
    #pragma warning restore CS0067
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
}
