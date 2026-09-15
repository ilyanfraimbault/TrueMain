namespace Data.Logging;

/// <summary>
/// Catalog of the process names stamped into log and crash documents
/// (<c>MongoLoggingOptions.ProcessName</c>): the two hosts that write to the
/// diagnostic store, and the two frontends whose server errors reach it through
/// <c>POST /internal/logs</c> (#1556). Shared by the Data-layer queries (to canonicalize a
/// case-insensitive filter into an indexable <c>$eq</c>) and the ops read
/// models (to populate the admin filter selects without a Mongo
/// <c>distinct</c>).
/// </summary>
public static class LogProcesses
{
    public static IReadOnlyList<string> KnownProcessNames { get; } = ["Api", "Ingestor", "Web", "Admin"];

    /// <summary>
    /// The hosts that write crash reports. The frontends forward errors but never
    /// crash-report — they have no crash capture and no Mongo connection — so the
    /// Crashes filter does not offer them.
    /// </summary>
    public static IReadOnlyList<string> CrashReportingProcessNames { get; } = ["Api", "Ingestor"];
}
