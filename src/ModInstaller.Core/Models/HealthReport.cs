namespace ModInstaller.Core.Models;

public class HealthReport
{
    public required string ModName { get; set; }
    public HealthStatus Status { get; set; } = HealthStatus.Healthy;
    public List<HealthIssue> Issues { get; set; } = [];
    public DateTime ScannedAt { get; set; } = DateTime.Now;
}

public class HealthIssue
{
    public required string Message { get; set; }
    public HealthStatus Severity { get; set; }
}
