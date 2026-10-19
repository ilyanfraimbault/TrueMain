namespace TrueMain.ReadModels.Ops;

/// <summary>Answer to <c>POST /ops/process-runs</c>: the id the script heartbeats and finishes with.</summary>
public sealed record ExternalProcessRunStartedResponse
{
    public Guid Id { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public DateTime StartedAtUtc { get; init; }
}
