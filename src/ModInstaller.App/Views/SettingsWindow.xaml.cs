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
        _settings.ModLibraryDirectory = LibraryDirBox.Text;
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
