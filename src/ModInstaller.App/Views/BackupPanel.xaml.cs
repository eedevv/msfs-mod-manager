using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ModInstaller.Core.Models;
using ModInstaller.Core.Services;
using ModInstaller.Core.Utils;

namespace ModInstaller.App.Views;

public partial class BackupPanel : UserControl
{
    private BackupManager? _backupManager;
    private AppSettings? _settings;
    private string? _communityFolder;
    private Action<string, string, ToastNotification.ToastType>? _showToast;
    private Func<Task>? _refreshMods;

    public BackupPanel()
    {
        InitializeComponent();
    }

    public void Initialize(AppSettings settings, string communityFolder,
        Action<string, string, ToastNotification.ToastType> showToast, Func<Task> refreshMods)
    {
        _backupManager = new BackupManager(settings, new FileOperations());
        _settings = settings;
        _communityFolder = communityFolder;
        _showToast = showToast;
        _refreshMods = refreshMods;
        RefreshList();
    }

    public void SetCommunityFolder(string communityFolder) => _communityFolder = communityFolder;

    public void RefreshList()
    {
        if (_backupManager == null) return;
        BackupList.ItemsSource = _backupManager.ListBackups();
    }

    private async void BackupAll_Click(object sender, RoutedEventArgs e)
    {
        if (_backupManager == null || string.IsNullOrWhiteSpace(_communityFolder) || !System.IO.Directory.Exists(_communityFolder))
        {
            _showToast?.Invoke("Community Folder", "Community folder not found — check Settings", ToastNotification.ToastType.Warning);
            return;
        }

        try
        {
            var path = await _backupManager.BackupAllModsAsync(_communityFolder);
            RefreshList();
            _showToast?.Invoke("Backup Complete", $"Saved to {System.IO.Path.GetFileName(path)}", ToastNotification.ToastType.Success);
        }
        catch (Exception ex)
        {
            _showToast?.Invoke("Backup Failed", ex.Message, ToastNotification.ToastType.Error);
        }
    }

    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_backupManager == null || _settings == null) return;
        try
        {
            System.IO.Directory.CreateDirectory(_settings.BackupDirectory);
            using var p = Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = _settings.BackupDirectory, UseShellExecute = true });
        }
        catch { }
    }

    private void CleanBackups_Click(object sender, RoutedEventArgs e)
    {
        if (_backupManager == null) return;
        var result = MessageBox.Show("Delete backups older than 30 days?", "Clean Backups",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            try
            {
                int deleted = _backupManager.CleanOldBackups(30);
                RefreshList();
                _showToast?.Invoke("Cleaned", $"Removed {deleted} old backup(s)", ToastNotification.ToastType.Info);
            }
            catch (Exception ex) { _showToast?.Invoke("Cleanup Failed", ex.Message, ToastNotification.ToastType.Error); }
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_backupManager == null || string.IsNullOrWhiteSpace(_communityFolder) || !System.IO.Directory.Exists(_communityFolder))
        {
            _showToast?.Invoke("Community Folder", "Community folder not found — check Settings", ToastNotification.ToastType.Warning);
            return;
        }
        var path = (sender as Button)?.Tag as string;
        if (path == null) return;

        var community = _communityFolder;
        var confirmation = MessageBox.Show(
            $"Restore this backup to:\n{community}\n\nChoose Yes to replace existing mods (original folders are retained in .msfsmm-recovery folders). Choose No to restore only if no destination exists.",
            "Confirm Restore", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (confirmation == MessageBoxResult.Cancel) return;
        try
        {
            await _backupManager.RestoreBackupAsync(path, community, overwrite: confirmation == MessageBoxResult.Yes);
            _showToast?.Invoke("Restored", System.IO.Path.GetFileName(path), ToastNotification.ToastType.Success);
        }
        catch (Exception ex)
        {
            _showToast?.Invoke("Restore Failed", ex.Message + " Some items may already have been restored.", ToastNotification.ToastType.Error);
        }
        finally
        {
            if (_refreshMods != null) await _refreshMods();
        }
    }

    private void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        var path = (sender as Button)?.Tag as string;
        if (path == null) return;

        if (MessageBox.Show($"Permanently delete this item?\n{path}", "Confirm Delete",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            using var operation = MutationGate.Enter();
            if (System.IO.Directory.Exists(path))
                FileOperations.SafeDeleteDirectory(path);
            else if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
            RefreshList();
            _showToast?.Invoke("Deleted", "", ToastNotification.ToastType.Info);
        }
        catch (Exception ex) { _showToast?.Invoke("Delete Failed", ex.Message, ToastNotification.ToastType.Error); }
    }
}
