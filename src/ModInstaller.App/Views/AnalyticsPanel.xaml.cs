using System.Collections.ObjectModel;
using System.Windows.Controls;
using ModInstaller.Core.Models;

namespace ModInstaller.App.Views;

public partial class AnalyticsPanel : UserControl
{
    public AnalyticsPanel()
    {
        InitializeComponent();
    }

    public void Refresh(ObservableCollection<Mod> mods)
    {
        if (mods.Count == 0) return;

        // Summary stats
        TotalMods.Text = mods.Count.ToString();
        var totalBytes = mods.Sum(m => m.SizeBytes);
        TotalSize.Text = FormatSize(totalBytes);
        JunctionCount.Text = mods.Count(m => m.LinkType == LinkType.Junction).ToString();
        DisabledCount.Text = mods.Count(m => m.Status == ModStatus.Disabled).ToString();

        // Group by type
        var typeGroups = mods
            .GroupBy(m => m.Type)
            .Select(g => new TypeStat
            {
                Type = g.Key.ToString(),
                Count = g.Count(),
                SizeBytes = g.Sum(m => m.SizeBytes),
                SizeDisplay = FormatSize(g.Sum(m => m.SizeBytes)),
                BarWidth = totalBytes > 0 ? Math.Max(4, (double)g.Sum(m => m.SizeBytes) / totalBytes * 300) : 4
            })
            .OrderByDescending(t => t.SizeBytes)
            .ToList();

        TypeBreakdown.ItemsSource = typeGroups;

        // Top 10 largest
        var largest = mods
            .OrderByDescending(m => m.SizeBytes)
            .Take(10)
            .Select((m, i) => new LargestMod
            {
                Rank = (i + 1).ToString(),
                Name = m.DisplayName,
                SizeDisplay = m.SizeDisplay
            })
            .ToList();

        LargestMods.ItemsSource = largest;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}

public class TypeStat
{
    public string Type { get; set; } = "";
    public int Count { get; set; }
    public long SizeBytes { get; set; }
    public string SizeDisplay { get; set; } = "";
    public double BarWidth { get; set; }
}

public class LargestMod
{
    public string Rank { get; set; } = "";
    public string Name { get; set; } = "";
    public string SizeDisplay { get; set; } = "";
}
