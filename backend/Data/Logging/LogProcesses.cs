namespace Data.Logging;

/// <summary>
/// Catalog of the process names stamped into log and crash documents
/// (<c>MongoLoggingOptions.ProcessName</c>): the two hosts that write to the
/// diagnostic store, the two frontends whose server errors reach it through
/// <c>POST /internal/logs</c> (#1556), and the edge proxy, whose failures the admin relays
/// there (#1580). Shared by the Data-layer queries (to canonicalize a
/// case-insensitive filter into an indexable <c>$eq</c>) and the ops read
/// models (to populate the admin filter selects without a Mongo
/// <c>distinct</c>).
/// </summary>
public static class LogProcesses
{
    public static IReadOnlyList<string> KnownProcessNames { get; } = ["Api", "Ingestor", "Web", "Admin", "Edge"];

    /// <summary>
    /// The hosts that write crash reports. The frontends and the edge forward errors but never
    /// crash-report — they have no crash capture and no Mongo connection — so the
    /// Crashes filter does not offer them.
    /// </summary>
    public static IReadOnlyList<string> CrashReportingProcessNames { get; } = ["Api", "Ingestor"];
}
