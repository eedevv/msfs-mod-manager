using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ModInstaller.Core.Models;
using ModInstaller.Core.Services;

namespace ModInstaller.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        LoadSettings();
        ApplyLocalization();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void ApplyLocalization()
    {
        // Window title
        Title = LocalizationService.Get("settings_title");

        // Header
        SettingsTitleText.Text = LocalizationService.Get("settings_title");

        // Language section
        LangSectionHeader.Text = LocalizationService.Get("settings_language").ToUpperInvariant();
        LangSectionDesc.Text = LocalizationService.Get("settings_language_desc");
        InterfaceLangLabel.Text = LocalizationService.Get("settings_interface_lang") + ":";

        // Community folder section
        CommunitySectionHeader.Text = LocalizationService.Get("settings_community").ToUpperInvariant();
        CommunitySectionDesc.Text = LocalizationService.Get("settings_community_desc");
        UseCustomFolderLabel.Text = LocalizationService.Get("settings_custom_folder");
        BrowseCustomBtn.Content = LocalizationService.Get("settings_browse");

        // Backup section
        BackupSectionHeader.Text = LocalizationService.Get("settings_backups").ToUpperInvariant();
        BackupSectionDesc.Text = LocalizationService.Get("settings_backups_desc");
        BackupDirLabel.Text = LocalizationService.Get("settings_backup_dir") + ":";
        BrowseBackupBtn.Content = LocalizationService.Get("settings_browse");

        // Library section
        LibrarySectionHeader.Text = LocalizationService.Get("settings_library").ToUpperInvariant();
        LibrarySectionDesc.Text = LocalizationService.Get("settings_library_desc");
        UseJunctionsLabel.Text = LocalizationService.Get("settings_junctions");
        LibraryDirLabel.Text = LocalizationService.Get("settings_library_dir") + ":";
        BrowseLibraryBtn.Content = LocalizationService.Get("settings_browse");

        // Watch folder section
        WatchSectionHeader.Text = LocalizationService.Get("settings_watch_folder").ToUpperInvariant();
        WatchSectionDesc.Text = LocalizationService.Get("settings_watch_folder_desc");
        WatchFolderLabel.Text = LocalizationService.Get("settings_watch_desc");
        WatchDirLabel.Text = LocalizationService.Get("settings_watch_dir") + ":";
        BrowseWatchBtn.Content = LocalizationService.Get("settings_browse");

        // General section
        GeneralSectionHeader.Text = LocalizationService.Get("settings_general").ToUpperInvariant();
        GeneralSectionDesc.Text = LocalizationService.Get("settings_general_desc");
        CheckUpdatesLabel.Text = LocalizationService.Get("settings_check_updates");
        MinimizeToTrayLabel.Text = LocalizationService.Get("settings_minimize_tray");

        // About section
        AboutSectionHeader.Text = LocalizationService.Get("settings_about").ToUpperInvariant();
        AboutCopyrightText.Text = "\u00A9 " + LocalizationService.Get("about_copyright");
        PatreonBtnText.Text = LocalizationService.Get("about_support");

        // Footer buttons
        ResetBtn.Content = LocalizationService.Get("settings_reset");
        CancelBtn.Content = LocalizationService.Get("settings_cancel");
        SaveBtn.Content = LocalizationService.Get("settings_save");
    }

    private void LoadSettings()
    {
        // Language
        var languages = LocalizationService.GetAvailableLanguages();
        foreach (var lang in languages)
        {
            var item = new ComboBoxItem { Content = lang.Value, Tag = lang.Key };
            LanguageCombo.Items.Add(item);
            if (lang.Key == _settings.Language)
                LanguageCombo.SelectedItem = item;
        }
        if (LanguageCombo.SelectedItem == null && LanguageCombo.Items.Count > 0)
            LanguageCombo.SelectedIndex = 0;

        // Community folder
        UseCustomFolderCheck.IsChecked = _settings.UseCustomFolder;
        CustomFolderBox.Text = _settings.CustomCommunityFolder2024 ?? _settings.CustomCommunityFolder2020 ?? "";

        // Paths
        BackupDirBox.Text = _settings.BackupDirectory;
        LibraryDirBox.Text = _settings.ModLibraryDirectory;
        UseJunctionsCheck.IsChecked = _settings.UseJunctions;

        // Watch folder
        WatchFolderCheck.IsChecked = _settings.WatchFolderEnabled;
        WatchFolderBox.Text = _settings.WatchFolderPath;

        // General
        CheckUpdatesCheck.IsChecked = _settings.CheckUpdatesOnStartup;
        MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
    }

    private void BrowseCustomFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseForFolder("Select Community Folder");
        if (path != null) CustomFolderBox.Text = path;
    }

    private void BrowseBackupDir_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseForFolder("Select Backup Directory");
        if (path != null) BackupDirBox.Text = path;
    }

    private void BrowseLibraryDir_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseForFolder("Select Mod Library Directory");
        if (path != null) LibraryDirBox.Text = path;
    }

    private void BrowseWatchFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseForFolder("Select Watch Folder");
        if (path != null) WatchFolderBox.Text = path;
    }

    private static string? BrowseForFolder(string description)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = description
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Reset all settings to their default values?",
            "Reset to Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        // Reset to default values
        var defaults = new AppSettings();

        UseCustomFolderCheck.IsChecked = false;
        CustomFolderBox.Text = "";
        BackupDirBox.Text = defaults.BackupDirectory;
        LibraryDirBox.Text = defaults.ModLibraryDirectory;
        UseJunctionsCheck.IsChecked = defaults.UseJunctions;
        WatchFolderCheck.IsChecked = defaults.WatchFolderEnabled;
        WatchFolderBox.Text = defaults.WatchFolderPath;
        CheckUpdatesCheck.IsChecked = defaults.CheckUpdatesOnStartup;
        MinimizeToTrayCheck.IsChecked = defaults.MinimizeToTray;

        // Reset language to English
        foreach (ComboBoxItem item in LanguageCombo.Items)
        {
            if (item.Tag is string code && code == "en")
            {
                LanguageCombo.SelectedItem = item;
                break;
            }
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Language
        if (LanguageCombo.SelectedItem is ComboBoxItem langItem && langItem.Tag is string langCode)
        {
            _settings.Language = langCode;
            LocalizationService.SetLanguage(langCode);
        }

        // Validate custom community folder
        _settings.UseCustomFolder = UseCustomFolderCheck.IsChecked == true;
        if (_settings.UseCustomFolder && !string.IsNullOrWhiteSpace(CustomFolderBox.Text))
        {
            var customPath = CustomFolderBox.Text.Trim();
            if (!Directory.Exists(customPath))
            {
                MessageBox.Show($"Community folder not found:\n{customPath}", "Invalid Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings.CustomCommunityFolder2024 = customPath;
        }

        // Validate backup dir (create if needed)
        if (!string.IsNullOrWhiteSpace(BackupDirBox.Text))
        {
            try { Directory.CreateDirectory(BackupDirBox.Text.Trim()); }
            catch
            {
                MessageBox.Show($"Cannot create backup directory:\n{BackupDirBox.Text}", "Invalid Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _settings.BackupDirectory = BackupDirBox.Text.Trim();
        _settings.ModLibraryDirectory = LibraryDirBox.Text.Trim();
        _settings.UseJunctions = UseJunctionsCheck.IsChecked == true;
        _settings.CheckUpdatesOnStartup = CheckUpdatesCheck.IsChecked == true;

        // Watch folder
        _settings.WatchFolderEnabled = WatchFolderCheck.IsChecked == true;
        _settings.WatchFolderPath = WatchFolderBox.Text.Trim();

        // General
        _settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
