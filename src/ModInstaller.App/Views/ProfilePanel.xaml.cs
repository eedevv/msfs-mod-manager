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
    private Func<bool>? _isBusy;

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

    public void SetInstaller(ModInstallerService installer, string communityFolder, Func<Task> refreshCallback, Func<bool> isBusy)
    {
        _installer = installer;
        _communityFolder = communityFolder;
        _refreshCallback = refreshCallback;
        _isBusy = isBusy;
        RefreshList();
    }

    public void SetCommunityFolder(string communityFolder)
    {
        _communityFolder = communityFolder;
        RefreshList();
    }

    public void RefreshList()
    {
        if (_db == null) return;
        var profiles = _db.GetAllProfiles(); // Legacy entries remain accessible but unassigned.
        if (!string.IsNullOrWhiteSpace(_communityFolder))
            profiles.InsertRange(0, _db.GetCommunityProfiles(_communityFolder));
        ProfileList.ItemsSource = profiles;
    }

    private bool IsReady(string community)
    {
        if (_mods == null || _isBusy?.Invoke() == true || MutationGate.IsBusy) return false;
        var disabled = new DisabledModStorage().GetDirectory(community);
        return _mods.All(mod =>
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(mod.FullPath));
            var expected = mod.Status == ModStatus.Disabled ? disabled : community;
            return parent != null && string.Equals(Path.TrimEndingDirectorySeparator(parent),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), StringComparison.OrdinalIgnoreCase);
        });
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_db == null || _mods == null) return;
        if (string.IsNullOrWhiteSpace(_communityFolder) || !Directory.Exists(_communityFolder))
        {
            _showToast?.Invoke("Community Folder", "Select an available Community folder first", ToastNotification.ToastType.Warning);
            return;
        }
        if (!IsReady(_communityFolder))
        {
            _showToast?.Invoke("Please wait", "Wait for the current Community folder scan to finish", ToastNotification.ToastType.Warning);
            return;
        }
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

        if (_db.GetCommunityProfiles(_communityFolder).Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) &&
            MessageBox.Show($"Replace the existing profile \"{name}\" for this Community folder?", "Confirm Replacement",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _db.SaveCommunityProfile(_communityFolder, profile);
        ProfileNameBox.Text = "";
        RefreshList();
        _showToast?.Invoke("Profile Saved", $"\"{name}\" with {profile.TotalModCount} mods", ToastNotification.ToastType.Success);
    }

    private async void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = (sender as Button)?.Tag as ModProfile;
        if (profile == null || _db == null || _installer == null || _mods == null) return;
        if (string.IsNullOrWhiteSpace(_communityFolder) || !Directory.Exists(_communityFolder))
        {
            _showToast?.Invoke("Community Folder", "Community folder not found — check Settings", ToastNotification.ToastType.Warning);
            return;
        }
        if (!IsReady(_communityFolder))
        {
            _showToast?.Invoke("Please wait", "Wait for the current Community folder scan to finish", ToastNotification.ToastType.Warning);
            return;
        }
        var community = _communityFolder;
        var key = DisabledModStorage.GetCommunityKey(community);
        if (profile.CommunityKey != null && profile.CommunityKey != key) return;
        if (profile.CommunityKey == null)
        {
            if (MessageBox.Show($"This legacy profile is not associated with a Community folder. Copy it to:\n{community}\n\n" +
                "Only continue if this profile belongs to that installation. The original legacy profile will be kept.",
                "Assign legacy profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try { _db.SaveCommunityProfile(community, profile, replace: false); }
            catch (Exception ex)
            {
                _showToast?.Invoke("Profile not assigned", $"Could not copy the profile without overwriting: {ex.Message}", ToastNotification.ToastType.Error);
                return;
            }
            RefreshList();
        }
        var name = profile.Name;

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
                if (_communityFolder == null || DisabledModStorage.GetCommunityKey(_communityFolder) != key)
                    throw new IOException("Community folder changed during profile loading; the operation stopped. Some mods may already have changed.");
                if (profile.EnabledMods.Contains(mod.FolderName, StringComparer.OrdinalIgnoreCase) && mod.Status == ModStatus.Disabled)
                {
                    await _installer.EnableModAsync(mod, community);
                    enabled++;
                }
                else if (profile.DisabledMods.Contains(mod.FolderName, StringComparer.OrdinalIgnoreCase) && mod.Status == ModStatus.Enabled)
                {
                    await _installer.DisableModAsync(mod);
                    disabled++;
                }
            }

            // Update last used date
            profile.LastUsedDate = DateTime.Now;
            _db.SaveCommunityProfile(community, profile);
            RefreshList();

            if (_refreshCallback != null) await _refreshCallback();

            _showToast?.Invoke("Profile Loaded",
                $"Enabled {enabled}, disabled {disabled} mods",
                ToastNotification.ToastType.Success);
        }
        catch (Exception ex)
        {
            if (_refreshCallback != null) await _refreshCallback();
            _showToast?.Invoke("Profile partially loaded", ex.Message, ToastNotification.ToastType.Error);
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = (sender as Button)?.Tag as ModProfile;
        if (profile == null || _db == null) return;
        if (MessageBox.Show($"Delete profile \"{profile.DisplayName}\"?", "Delete Profile",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (profile.CommunityKey == null) _db.DeleteProfile(profile.Name);
        else
        {
            if (string.IsNullOrWhiteSpace(_communityFolder) ||
                DisabledModStorage.GetCommunityKey(_communityFolder) != profile.CommunityKey) return;
            _db.DeleteCommunityProfile(_communityFolder, profile.Name);
        }
        RefreshList();
        _showToast?.Invoke("Deleted", $"Profile \"{profile.Name}\" removed", ToastNotification.ToastType.Info);
    }
}
