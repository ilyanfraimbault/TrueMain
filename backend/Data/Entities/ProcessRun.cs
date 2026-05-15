using System.Text.Json;

namespace Data.Entities;

public class ProcessRun
{
    public Guid Id { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; }

    public DateTime FinishedAtUtc { get; set; }

    public int DurationMs { get; set; }

    public ProcessRunStatus Status { get; set; }

    public string? Error { get; set; }

    public string? Host { get; set; }

    public JsonDocument? Summary { get; set; }
}

public enum ProcessRunStatus
{
    Success = 0,
    Failed = 1
}
