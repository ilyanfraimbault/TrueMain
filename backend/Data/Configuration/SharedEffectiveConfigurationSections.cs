using Core.Options;
using Data.Logging.Mongo;

namespace Data.Configuration;

/// <summary>
/// The sections both hosts bind, declared once (#1034).
///
/// <para>
/// <c>MainAnalysis</c>, <c>Database</c> and <c>MongoLogging</c> are read by the Api and by the
/// Ingestor from the same keys, and the two can genuinely hold different values — preprod
/// shortens <c>MongoLogging:LogsRetention</c> on the Api container only, and the page exists to
/// make that visible. What must not diverge is the *description* of a section, and above all
/// <c>MongoLogging</c>'s include-list: two hand-maintained copies of "which properties are safe
/// to publish" is exactly the shape of mistake that eventually publishes a connection string.
/// </para>
/// </summary>
public static class SharedEffectiveConfigurationSections
{
    /// <summary>The champion-mastery thresholds that decide who counts as a main.</summary>
    public static EffectiveConfigurationSectionDescriptor MainAnalysis { get; } = new()
    {
        SectionName = "MainAnalysis",
        OptionsType = typeof(MainAnalysisOptions),
        Title = "Main analysis",
        Description =
            "Decides who counts as a main: the play-rate threshold, the floor the "
            + "coverage-adaptive relaxation may reach for a rarely-played champion, and how many "
            + "recent matches a verdict is computed over."
    };

    /// <summary>Schema migration behaviour.</summary>
    public static EffectiveConfigurationSectionDescriptor Database { get; } = new()
    {
        SectionName = DatabaseOptions.SectionName,
        OptionsType = typeof(DatabaseOptions),
        Title = "Database",
        Description = "Whether this process applies pending EF migrations when it starts."
    };

    /// <summary>
    /// The Mongo-backed observability stack: what is persisted, and how long each collection
    /// keeps it.
    ///
    /// <para>
    /// The only section in either catalog that both carries a secret and is worth exposing, and
    /// therefore the reason the include-list exists. The retention windows are what an operator
    /// comes here for; <c>ConnectionString</c>, the database name, the collection names and the
    /// crash-file path are all omitted.
    /// </para>
    /// </summary>
    public static EffectiveConfigurationSectionDescriptor MongoLogging { get; } = new()
    {
        SectionName = MongoLoggingOptions.SectionName,
        OptionsType = typeof(MongoLoggingOptions),
        Title = "Logging & retention",
        Description =
            "What this process persists to Mongo, and how long each collection keeps it. "
            + "Retention is enforced by native TTL indexes, so a window of zero means the "
            + "collection is never pruned.",
        IncludeProperties =
        [
            nameof(MongoLoggingOptions.Enabled),
            nameof(MongoLoggingOptions.MinimumLevel),
            nameof(MongoLoggingOptions.LogsRetention),
            nameof(MongoLoggingOptions.RiotApiCallsRetention),
            nameof(MongoLoggingOptions.DbTableSizeSnapshotsRetention),
            nameof(MongoLoggingOptions.CandidateStockSnapshotsRetention),
            nameof(MongoLoggingOptions.ProcessRunsRetention),
            nameof(MongoLoggingOptions.CrashesRetention),
            nameof(MongoLoggingOptions.Capacity),
            nameof(MongoLoggingOptions.BatchSize),
            nameof(MongoLoggingOptions.FlushInterval),
            nameof(MongoLoggingOptions.CrashLogTailSize),
            nameof(MongoLoggingOptions.CrashFileMaxBytes),
            nameof(MongoLoggingOptions.CrashMongoWriteTimeout)
        ],
        Notices =
        [
            new EffectiveConfigurationNotice(
                nameof(MongoLoggingOptions.LogsRetention),
                UnsetCondition.ZeroOrNegative,
                "No TTL index: diagnostic logs are kept forever and the collection grows without bound."),
            new EffectiveConfigurationNotice(
                nameof(MongoLoggingOptions.RiotApiCallsRetention),
                UnsetCondition.ZeroOrNegative,
                "No TTL index: Riot usage rollups are kept forever, well past the 7-day window the panel reads."),
            new EffectiveConfigurationNotice(
                nameof(MongoLoggingOptions.DbTableSizeSnapshotsRetention),
                UnsetCondition.ZeroOrNegative,
                "No TTL index: daily storage snapshots are kept forever."),
            new EffectiveConfigurationNotice(
                nameof(MongoLoggingOptions.CandidateStockSnapshotsRetention),
                UnsetCondition.ZeroOrNegative,
                "No TTL index: hourly candidate-stock snapshots are kept forever — the densest of the "
                + "snapshot collections, at one document per platform and status per hour."),
            new EffectiveConfigurationNotice(
                nameof(MongoLoggingOptions.ProcessRunsRetention),
                UnsetCondition.ZeroOrNegative,
                "No TTL index: recorded process runs are kept forever. The Overview's ingestion "
                + "chart states this window when a requested range exceeds it."),
            new EffectiveConfigurationNotice(
                nameof(MongoLoggingOptions.CrashesRetention),
                UnsetCondition.ZeroOrNegative,
                "No TTL index: crash reports are kept forever.")
        ]
    };
}
