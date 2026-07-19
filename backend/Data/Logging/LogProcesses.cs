namespace Data.Logging;

/// <summary>
/// Catalog of the process names stamped into log and crash documents
/// (<c>MongoLoggingOptions.ProcessName</c>): the two hosts that write to the
/// diagnostic store. Shared by the Data-layer queries (to canonicalize a
/// case-insensitive filter into an indexable <c>$eq</c>) and the ops read
/// models (to populate the admin filter selects without a Mongo
/// <c>distinct</c>).
/// </summary>
public static class LogProcesses
{
    public static IReadOnlyList<string> KnownProcessNames { get; } = ["Api", "Ingestor"];
}
