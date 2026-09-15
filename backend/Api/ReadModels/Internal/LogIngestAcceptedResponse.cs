namespace TrueMain.ReadModels.Internal;

/// <summary>Response of <c>POST /internal/logs</c>: how many entries were queued for persistence.</summary>
public sealed record LogIngestAcceptedResponse
{
    /// <summary>
    /// Entries handed to the log channel. Lower than the batch only when the log store
    /// is off in this environment; the channel's own overflow is reported as a
    /// <c>LogRecordsDropped</c> row, not here.
    /// </summary>
    public int Accepted { get; init; }
}
