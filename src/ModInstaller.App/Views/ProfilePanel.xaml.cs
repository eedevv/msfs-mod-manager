using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ModInstaller.Core.Database;
using ModInstaller.Core.Models;
using ModInstaller.Core.Services;
using ModInstaller.Core.Utils;

namespace ModInstaller.App.Views;

public partial class ProfilePanel : UserControl
{
    private ModDatabase? _db;
    private ObservableCollection<Mod>? _mods;
    private ModInstallerService? _installer;
    private string? _communityFolder;
    private Action<string, string, ToastNotification.ToastType>? _showToast;
    private Func<Task>? _refreshCallback;

    public ProfilePanel()
    {
        InitializeComponent();
    }

    public void Initialize(ModDatabase db, ObservableCollection<Mod> mods,
        Action<string, string, ToastNotification.ToastType> showToast)
    {
        _db = db;
        _mods = mods;
        _showToast = showToast;
        RefreshList();
    }

    public void SetInstaller(ModInstallerService installer, string communityFolder, Func<Task> refreshCallback)
    {
        _installer = installer;
        _communityFolder = communityFolder;
        _refreshCallback = refreshCallback;
    }

    public void SetCommunityFolder(string communityFolder) => _communityFolder = communityFolder;

    public void RefreshList()
    {
        if (_db == null) return;
        ProfileList.ItemsSource = _db.GetAllProfiles();
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null || _mods == null) return;
        var name = ProfileNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _showToast?.Invoke("Error", "Enter a profile name", ToastNotification.ToastType.Warning);
            return;
        }

        var profile = new ModProfile
        {
            Name = name,
            EnabledMods = _mods.Where(m => m.Status == ModStatus.Enabled).Select(m => m.FolderName).ToList(),
            DisabledMods = _mods.Where(m => m.Status == ModStatus.Disabled).Select(m => m.FolderName).ToList()
        };

        _db.SaveProfile(profile);
        ProfileNameBox.Text = "";
        RefreshList();
        _showToast?.Invoke("Profile Saved", $"\"{name}\" with {profile.TotalModCount} mods", ToastNotification.ToastType.Success);
    }

    private async void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = (sender as Button)?.Tag as string;
        if (name == null || _db == null || _installer == null || _mods == null) return;
        if (string.IsNullOrWhiteSpace(_communityFolder) || !Directory.Exists(_communityFolder))
        {
            _showToast?.Invoke("Community Folder", "Community folder not found — check Settings", ToastNotification.ToastType.Warning);
            return;
        }

        var profiles = _db.GetAllProfiles();
        var profile = profiles.FirstOrDefault(p => p.Name == name);
        if (profile == null) return;

        var result = MessageBox.Show(
            $"Load profile \"{name}\"?\n\nThis will enable {profile.EnabledMods.Count} and disable {profile.DisabledMods.Count} mods.",
            "Load Profile", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _showToast?.Invoke("Loading Profile...", name, ToastNotification.ToastType.Info);

        try
        {
            int enabled = 0, disabled = 0;

            foreach (var mod in _mods.ToList())
            {
                if (profile.EnabledMods.Contains(mod.FolderName) && mod.Status == ModStatus.Disabled)
                {
                    await _installer.EnableModAsync(mod, _communityFolder);
                    enabled++;
                }
                else if (profile.DisabledMods.Contains(mod.FolderName) && mod.Status == ModStatus.Enabled)
                {
                    await _installer.DisableModAsync(mod);
                    disabled++;
                }
            }

            // Update last used date
            profile.LastUsedDate = DateTime.Now;
            _db.SaveProfile(profile);
            RefreshList();

            if (_refreshCallback != null) await _refreshCallback();

            _showToast?.Invoke("Profile Loaded",
                $"Enabled {enabled}, disabled {disabled} mods",
                ToastNotification.ToastType.Success);
        }
        catch (Exception ex)
        {
            _showToast?.Invoke("Error", ex.Message, ToastNotification.ToastType.Error);
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = (sender as Button)?.Tag as string;
        if (name == null || _db == null) return;
        _db.DeleteProfile(name);
        RefreshList();
        _showToast?.Invoke("Deleted", $"Profile \"{name}\" removed", ToastNotification.ToastType.Info);
    }
}
