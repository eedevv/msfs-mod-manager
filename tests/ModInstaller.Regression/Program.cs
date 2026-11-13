using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using ModInstaller.Core.Database;
using ModInstaller.Core.Services;
using ModInstaller.Core.Utils;
using System.Text.Json;
using System.Xml.Linq;
using ModInstaller.App.ViewModels;
using ModInstaller.Core.Models;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            TestSizeSort();
            TestFolderResolutionAndPersistence();
            TestFolderSelection();
            TestScopedDisabledStorage();
            TestScopedProfiles();
            TestConflictScan();
            TestFileSafety();
            Console.WriteLine("Regression checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void TestSizeSort()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var column = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"))
            .Descendants().Single(e => (string?)e.Attribute(x + "Name") == "ColSize");
        var sortPath = (string?)column.Attribute("SortMemberPath");
        Check(sortPath == nameof(Mod.SizeBytes), "Size column must sort on byte count.");
        Check(((string?)column.Attribute("Binding"))?.Contains(nameof(Mod.SizeDisplay)) == true,
            "Size column should still show formatted sizes.");

        var vm = new MainViewModel();
        foreach (var size in new long[] { 1024L * 1024 * 1024, 2 * 1024, 950 * 1024 * 1024, 100, 10 * 1024 })
            vm.Mods.Add(new Mod { FolderName = size.ToString(), FullPath = "", SizeBytes = size });
        vm.ModsView.SortDescriptions.Add(new SortDescription(sortPath!, ListSortDirection.Ascending));
        var ascending = vm.ModsView.Cast<Mod>().Select(m => m.SizeBytes).ToArray();
        Check(ascending.SequenceEqual(ascending.OrderBy(n => n)), "Ascending size order is incorrect.");
        vm.ModsView.SortDescriptions.Clear();
        vm.ModsView.SortDescriptions.Add(new SortDescription(sortPath!, ListSortDirection.Descending));
        Check(vm.ModsView.Cast<Mod>().Select(m => m.SizeBytes).SequenceEqual(ascending.Reverse()),
            "Descending size order is incorrect.");
    }

    private static void TestFolderResolutionAndPersistence()
    {
        var settings = new AppSettings { UseCustomFolder = true, ActiveSim = SimVersion.MSFS2020 };
        settings.SetCustomCommunityFolder(SimVersion.MSFS2020, @"D:\Custom2020");
        settings.SetCustomCommunityFolder(SimVersion.MSFS2024, @"E:\Custom2024");
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Check(restored.ResolveCommunityFolder(SimVersion.MSFS2020, @"C:\Detected2020") == @"D:\Custom2020", "2020 custom path lost.");
        Check(restored.ResolveCommunityFolder(SimVersion.MSFS2024, @"C:\Detected2024") == @"E:\Custom2024", "2024 custom path lost.");
        Check(restored.ResolveCommunityFolder(SimVersion.MSFS2024, null) == @"E:\Custom2024", "Custom path must work without detection.");
        restored.UseCustomFolder = false;
        Check(restored.ResolveCommunityFolder(SimVersion.MSFS2020, @"C:\Detected2020") == @"C:\Detected2020", "Disabling custom path must use detected path.");
        restored.UseCustomFolder = true;
        restored.CustomCommunityFolder2020 = null;
        Check(restored.ResolveCommunityFolder(SimVersion.MSFS2020, @"C:\Detected2020") == @"C:\Detected2020", "Unset per-sim override should use detected path.");
    }

    private static void TestFolderSelection()
    {
        // Non-existent paths exercise selection and validation without touching user mods.
        var root = Path.Combine(Path.GetTempPath(), "MSFSRegression_" + Guid.NewGuid().ToString("N"));
        var detected2020 = Path.Combine(root, "detected2020");
        var detected2024 = Path.Combine(root, "detected2024");
        var custom2020 = Path.Combine(root, "custom2020");
        var custom2024 = Path.Combine(root, "custom2024");
        var vm = new MainViewModel();
        vm.Settings.UseCustomFolder = true;
        vm.Settings.CustomCommunityFolder2020 = custom2020;
        vm.Settings.CustomCommunityFolder2024 = custom2024;
        vm.SelectedInstallation = new SimInstallation { Version = SimVersion.MSFS2020, CommunityFolder = detected2020 };
        Check(vm.CommunityFolderPath == custom2020, "Selecting 2020 must use its custom folder.");
        Check(vm.StatusText.Contains("not found"), "An unavailable custom folder must report an error.");
        vm.Mods.Add(new Mod { FolderName = "old", FullPath = "old" });
        vm.SelectedInstallation = new SimInstallation { Version = SimVersion.MSFS2024, CommunityFolder = detected2024 };
        Check(vm.CommunityFolderPath == custom2024, "Selecting 2024 must use its custom folder.");
        Check(vm.Mods.Count == 0, "Changing to an unavailable folder must clear old mods.");
        vm.Settings.UseCustomFolder = false;
        vm.ApplyCommunityFolderAsync().GetAwaiter().GetResult();
        Check(vm.CommunityFolderPath == detected2024, "Disabling the override must restore the detected path.");
        vm.Settings.UseCustomFolder = true;
        vm.Settings.CustomCommunityFolder2024 = Path.Combine(root, "changed");
        vm.ApplyCommunityFolderAsync().GetAwaiter().GetResult();
        Check(vm.CommunityFolderPath == vm.Settings.CustomCommunityFolder2024, "Saving a new path must apply immediately.");

        var noDetection = new MainViewModel();
        noDetection.Settings.UseCustomFolder = true;
        noDetection.Settings.CustomCommunityFolder2024 = custom2024;
        noDetection.ApplyCommunityFolderAsync().GetAwaiter().GetResult();
        Check(noDetection.CommunityFolderPath == custom2024, "Custom folder should work without simulator detection.");
    }
    private static void TestScopedDisabledStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "MSFSStorageTest_" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "legacy");
        var first = Path.Combine(root, "community2020");
        var second = Path.Combine(root, "community2024");
        var storage = new DisabledModStorage(Path.Combine(root, "scoped"), legacy);
        try
        {
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            Directory.CreateDirectory(Path.Combine(legacy, "legacy-mod"));
            File.WriteAllText(Path.Combine(legacy, "legacy-mod", "keep.txt"), "keep");
            Check(storage.GetDirectory(first) != storage.GetDirectory(second), "Installations must have separate disabled folders.");
            Check(storage.GetDirectory(first) == storage.GetDirectory(first.ToUpperInvariant()), "Windows paths should be case-insensitive.");
            var result = storage.MigrateLegacy(first);
            Check(result.Moved == 1 && result.Errors.Count == 0, "Legacy mod should be assigned to the chosen installation.");
            Check(File.Exists(Path.Combine(storage.GetDirectory(first), "legacy-mod", "keep.txt")), "Legacy data should survive migration.");
            Check(!Directory.Exists(Path.Combine(storage.GetDirectory(second), "legacy-mod")), "Legacy mod must not appear in the other installation.");
            Directory.CreateDirectory(Path.Combine(legacy, "legacy-mod"));
            File.WriteAllText(Path.Combine(legacy, "legacy-mod", "new.txt"), "do not overwrite");
            result = storage.MigrateLegacy(first);
            Check(result.Moved == 0 && result.Errors.Count == 1 && File.Exists(Path.Combine(legacy, "legacy-mod", "new.txt")),
                "Migration collisions must be preserved in legacy storage.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void TestScopedProfiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "MSFSProfileTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "2020");
            var second = Path.Combine(root, "2024");
            using (var db = new ModDatabase(Path.Combine(root, "profiles.db")))
            {
                db.SaveProfile(new ModProfile { Name = "legacy", EnabledMods = ["old"] });
                db.SaveCommunityProfile(first, new ModProfile { Name = "shared", EnabledMods = ["first"] });
                db.SaveCommunityProfile(second, new ModProfile { Name = "shared", EnabledMods = ["second"] });
                Check(db.GetCommunityProfiles(first.ToUpperInvariant()).Single().EnabledMods.Single() == "first", "First profile must be isolated and path-case independent.");
                Check(db.GetCommunityProfiles(second).Single().EnabledMods.Single() == "second", "Same-named profile must not overwrite another Community folder.");
                var legacy = db.GetAllProfiles().Single();
                Check(legacy.CommunityKey == null && legacy.EnabledMods.Single() == "old", "Existing profiles must remain unassigned.");
                db.SaveCommunityProfile(first, legacy, replace: false);
                Check(db.GetAllProfiles().Single().CommunityKey == null, "Assigning a legacy profile must keep the original.");
                var rejected = false;
                try { db.SaveCommunityProfile(first, new ModProfile { Name = "SHARED", EnabledMods = ["bad"] }, replace: false); }
                catch (Microsoft.Data.Sqlite.SqliteException) { rejected = true; }
                Check(rejected && db.GetCommunityProfiles(first).Single(p => p.Name == "shared").EnabledMods.Single() == "first", "Legacy assignment must not overwrite a same-named scoped profile.");
                db.DeleteCommunityProfile(first, "shared");
                Check(db.GetCommunityProfiles(second).Count == 1 && db.GetAllProfiles().Count == 1, "Deleting a scoped profile must not delete another installation or legacy profile.");
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void TestConflictScan()
    {
        var root = Path.Combine(Path.GetTempPath(), "MSFSConflictTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "shared.txt"), "one");
            File.WriteAllText(Path.Combine(second, "shared.txt"), "two");
            using var db = new ModDatabase(Path.Combine(root, "test.db"));
            var engine = new ConflictEngine(db);
            var mods = new List<Mod> { new() { FolderName = "first", FullPath = first }, new() { FolderName = "second", FullPath = second } };
            var found = engine.ScanForConflictsAsync(mods).GetAwaiter().GetResult();
            Check(found.Count == 1 && found[0].ConflictingFiles.Contains("shared.txt"), "Matching paths should be reported.");
            Directory.Delete(second, true);
            var failed = false;
            try { engine.ScanForConflictsAsync(mods).GetAwaiter().GetResult(); }
            catch (IOException ex) { failed = ex.Message.Contains("incomplete"); }
            Check(failed, "Unavailable mods must fail the scan instead of reporting no conflicts.");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void TestFileSafety()
    {
        var root = Path.Combine(Path.GetTempPath(), "MSFSRegression_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var dest = Path.Combine(root, "dest");
            Directory.CreateDirectory(dest);
            Check(FileOperations.IsPathWithinDirectory(dest, Path.Combine(dest, "a")), "Child should be within destination.");
            Check(!FileOperations.IsPathWithinDirectory(dest, Path.Combine(root, "dest-other", "a")), "Sibling prefix must not pass containment.");

            var zip = Path.Combine(root, "bad.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("../dest-other/escape.txt").Open()))
                writer.Write("bad");
            var rejected = false;
            try { new FileOperations().ExtractZipAsync(zip, dest).GetAwaiter().GetResult(); }
            catch (IOException) { rejected = true; }
            Check(rejected && !File.Exists(Path.Combine(root, "dest-other", "escape.txt")), "ZIP traversal must be rejected.");

            var source = Path.Combine(root, "source", "test-mod");
            var community = Path.Combine(root, "community");
            var library = Path.Combine(root, "library");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(community);
            File.WriteAllText(Path.Combine(source, "manifest.json"), "{}");
            var settings = new AppSettings { ModLibraryDirectory = library, BackupDirectory = Path.Combine(root, "backups") };
            using var db = new ModDatabase(Path.Combine(root, "test.db"));
            var service = new ModInstallerService(db, new ModScanner(), new FileOperations(), settings);
            var existing = Path.Combine(community, "test-mod");
            Directory.CreateDirectory(existing);
            File.WriteAllText(Path.Combine(existing, "keep.txt"), "original");
            rejected = false;
            try { service.InstallModAsync(source, community, SimVersion.MSFS2024, false).GetAwaiter().GetResult(); }
            catch (IOException) { rejected = true; }
            Check(rejected && File.ReadAllText(Path.Combine(existing, "keep.txt")) == "original", "Existing destination must not be overwritten.");

            service.InstallModAsync(source, community, SimVersion.MSFS2024, false,
                confirmReplacement: _ => true).GetAwaiter().GetResult();
            Check(File.Exists(Path.Combine(existing, "manifest.json")), "Approved replacement should install staged content.");
            Check(Directory.GetDirectories(community, RecoverableSwap.RecoveryPrefix + "*")
                .Any(r => File.Exists(Path.Combine(r, "original", "keep.txt"))), "Approved replacement must preserve the prior mod.");

            var rootZip = Path.Combine(root, "root-pack.zip");
            ZipFile.CreateFromDirectory(source, rootZip);
            service.InstallModAsync(rootZip, community, SimVersion.MSFS2024, false).GetAwaiter().GetResult();
            Check(File.Exists(Path.Combine(community, "root-pack", "manifest.json")), "Root-level archive should use a stable filename.");

            var multiRoot = Path.Combine(root, "multi");
            Directory.CreateDirectory(Path.Combine(multiRoot, "first-mod"));
            Directory.CreateDirectory(Path.Combine(multiRoot, "second-mod"));
            File.WriteAllText(Path.Combine(multiRoot, "first-mod", "manifest.json"), "{}");
            File.WriteAllText(Path.Combine(multiRoot, "second-mod", "manifest.json"), "{}");
            var multiZip = Path.Combine(root, "multi.zip");
            ZipFile.CreateFromDirectory(multiRoot, multiZip);
            service.InstallModAsync(multiZip, community, SimVersion.MSFS2024, false).GetAwaiter().GetResult();
            Check(File.Exists(Path.Combine(community, "first-mod", "manifest.json")) &&
                  File.Exists(Path.Combine(community, "second-mod", "manifest.json")), "Multiple archive packages should all be installed.");

            var secondCommunity = Path.Combine(root, "community-b");
            Directory.CreateDirectory(secondCommunity);
            // A same-named package may be managed independently for two installations.
            service.InstallModAsync(source, secondCommunity, SimVersion.MSFS2020, true).GetAwaiter().GetResult();
            var thirdCommunity = Path.Combine(root, "community-c");
            Directory.CreateDirectory(thirdCommunity);
            service.InstallModAsync(source, thirdCommunity, SimVersion.MSFS2024, true).GetAwaiter().GetResult();
            var targetB = JunctionHelper.GetJunctionTarget(Path.Combine(secondCommunity, "test-mod"));
            var targetC = JunctionHelper.GetJunctionTarget(Path.Combine(thirdCommunity, "test-mod"));
            Check(targetB != null && targetC != null && targetB != targetC, "Each Community folder must use its own library target.");

            var oldTarget = targetB!;
            service.InstallModAsync(source, secondCommunity, SimVersion.MSFS2020, true,
                confirmReplacement: _ => true).GetAwaiter().GetResult();
            var newTarget = JunctionHelper.GetJunctionTarget(Path.Combine(secondCommunity, "test-mod"));
            Check(newTarget != null && newTarget != oldTarget && Directory.Exists(oldTarget),
                "Junction replacement must preserve the original target and use a separate new target.");

            var external = Path.Combine(root, "external");
            var link = Path.Combine(community, "external-mod");
            Directory.CreateDirectory(external);
            File.WriteAllText(Path.Combine(external, "keep.txt"), "external");
            JunctionHelper.CreateJunction(link, external);
            service.UninstallMod(new Mod { FolderName = "external-mod", FullPath = link }, createBackup: true);
            Check(!Directory.Exists(link) && File.Exists(Path.Combine(external, "keep.txt")), "Uninstall must preserve external link targets.");

            var nestedContainer = Path.Combine(root, "delete-container");
            Directory.CreateDirectory(nestedContainer);
            JunctionHelper.CreateJunction(Path.Combine(nestedContainer, "external-link"), external);
            FileOperations.SafeDeleteDirectory(nestedContainer);
            Check(!Directory.Exists(nestedContainer) && File.Exists(Path.Combine(external, "keep.txt")),
                "Deleting a folder must not follow nested junctions or delete their targets.");

            var backupManager = new BackupManager(settings, new FileOperations());
            var full = Path.Combine(settings.BackupDirectory, "FullBackup_20260101_120000");
            Directory.CreateDirectory(Path.Combine(full, "mod-a"));
            Directory.CreateDirectory(Path.Combine(full, "mod-b"));
            File.WriteAllText(Path.Combine(full, "mod-a", "a.txt"), "a");
            File.WriteAllText(Path.Combine(full, "mod-b", "b.txt"), "b");
            backupManager.RestoreBackupAsync(full, community).GetAwaiter().GetResult();
            Check(File.Exists(Path.Combine(community, "mod-a", "a.txt")) && File.Exists(Path.Combine(community, "mod-b", "b.txt")), "Full backup must restore each mod at Community root.");
            File.WriteAllText(Path.Combine(community, "mod-a", "a.txt"), "previous");
            backupManager.RestoreBackupAsync(full, community, overwrite: true).GetAwaiter().GetResult();
            Check(File.ReadAllText(Path.Combine(community, "mod-a", "a.txt")) == "a", "Restore overwrite must install the backup.");
            var recoveries = Directory.GetDirectories(community, RecoverableSwap.RecoveryPrefix + "*");
            Check(recoveries.Any(r => File.Exists(Path.Combine(r, "original", "a.txt")) &&
                File.ReadAllText(Path.Combine(r, "original", "a.txt")) == "previous"), "Restore must retain the displaced data.");
            Check(RecoverableSwap.RecoverInterrupted(community).Count == 0, "Recovery scan should succeed.");
            Directory.Delete(Path.Combine(community, "mod-a"), true);
            RecoverableSwap.RecoverInterrupted(community);
            Check(!Directory.Exists(Path.Combine(community, "mod-a")), "A committed old version must not reappear after uninstall.");

            var swapDest = Path.Combine(community, "rollback-test");
            var swapStage = Path.Combine(community, ".msfsmm-restore-injected");
            Directory.CreateDirectory(swapDest);
            Directory.CreateDirectory(swapStage);
            File.WriteAllText(Path.Combine(swapDest, "old.txt"), "old");
            rejected = false;
            try { RecoverableSwap.Commit(swapStage, swapDest, true, () => throw new IOException("Injected failure")); }
            catch (IOException) { rejected = true; }
            Check(rejected && File.Exists(Path.Combine(swapDest, "old.txt")), "Failed promotion must restore the original.");
            var compressed = Path.Combine(settings.BackupDirectory, "zip-mod_20260101_120000.zip");
            ZipFile.CreateFromDirectory(source, compressed, CompressionLevel.Optimal, includeBaseDirectory: true);
            backupManager.RestoreBackupAsync(compressed, community).GetAwaiter().GetResult();
            Check(File.Exists(Path.Combine(community, "zip-mod", "manifest.json")), "ZIP backup must restore under the mod name.");
            rejected = false;
            try { backupManager.RestoreBackupAsync(compressed, community).GetAwaiter().GetResult(); }
            catch (IOException) { rejected = true; }
            Check(rejected, "ZIP restore must not silently overwrite a mod.");

            var interruptedDest = Path.Combine(community, "interrupted-mod");
            var interrupted = Path.Combine(community, RecoverableSwap.RecoveryPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(interrupted, "original"));
            File.WriteAllText(Path.Combine(interrupted, "destination.txt"), "interrupted-mod");
            File.WriteAllText(Path.Combine(interrupted, "pending"), "");
            File.WriteAllText(Path.Combine(interrupted, "original", "data.txt"), "safe");
            var warnings = RecoverableSwap.RecoverInterrupted(community);
            Check(warnings.Count == 1 && !Directory.Exists(interruptedDest) &&
                File.Exists(Path.Combine(interrupted, "original", "data.txt")),
                "Ambiguous interruption must retain the original and require manual recovery, not resurrect a removed mod.");

            var unrelated = Path.Combine(settings.BackupDirectory, "unrelated");
            Directory.CreateDirectory(unrelated);
            Directory.SetCreationTime(unrelated, DateTime.Now.AddDays(-50));
            backupManager.CleanOldBackups(30);
            Check(Directory.Exists(unrelated), "Cleanup must not delete an unmarked folder.");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                RemoveTestJunctions(root);
                Directory.Delete(root, true);
            }
        }
    }

    private static void RemoveTestJunctions(string path)
    {
        foreach (var dir in Directory.GetDirectories(path))
        {
            if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
                JunctionHelper.RemoveJunction(dir);
            else
                RemoveTestJunctions(dir);
        }
    }

}
