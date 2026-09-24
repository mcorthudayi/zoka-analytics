using System.ComponentModel.DataAnnotations;

namespace ZokaAnalytics.Models;

public class SyncLog
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Step { get; set; } = string.Empty;

    public SyncStatus Status { get; set; } = SyncStatus.Running;

    public int ItemsProcessed { get; set; }
    public int ApiCallsUsed { get; set; }

    [MaxLength(2000)]
    public string? Message { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}