namespace Data.Pipeline;

/// <summary>
/// The run-summary keys that code other than the run viewer reads back: the Riot budget
/// ledgers charge a day's spend from them and the Api's ops series sum them. Each one is
/// pinned on the summary record that writes it with <c>[JsonPropertyName]</c>, so renaming
/// a record property cannot silently change a key a reader depends on. Summary keys not
/// listed here are display-only and follow their record.
/// </summary>
public static class RunSummaryFields
{
    /// <summary>Keys of <see cref="PipelineCatalog.Discovery"/> runs.</summary>
    public static class Discovery
    {
        /// <summary>The per-platform breakdown; the run has no total of its own.</summary>
        public const string Platforms = "platforms";

        /// <summary>Per platform: candidates the ladder walk added (the funnel's ladder intake).</summary>
        public const string CandidatesInserted = "candidatesInserted";
    }

    /// <summary>Keys of <see cref="PipelineCatalog.Harvest"/> runs, no-work passes included.</summary>
    public static class Harvest
    {
        /// <summary>Candidates harvested from stored matches (the funnel's harvest intake).</summary>
        public const string CandidatesInserted = "candidatesInserted";
    }

    /// <summary>Keys of <see cref="PipelineCatalog.ManualSeed"/> runs.</summary>
    public static class ManualSeed
    {
        /// <summary>Seeded candidates queued for ingestion (the funnel's manual intake).</summary>
        public const string CandidatesQueued = "candidatesQueued";
    }

    /// <summary>Keys of <see cref="PipelineCatalog.Scoring"/> runs.</summary>
    public static class Scoring
    {
        /// <summary>The per-platform breakdown; the run has no total of its own.</summary>
        public const string Platforms = "platforms";

        /// <summary>Per platform: candidates scored.</summary>
        public const string Scored = "scored";

        /// <summary>Per platform: candidates promoted to the ingestion queue.</summary>
        public const string Queued = "queued";
    }

    /// <summary>Keys of <see cref="PipelineCatalog.MatchIngestion"/> runs.</summary>
    public static class MatchIngestion
    {
        /// <summary>
        /// Accounts whose candidates cleared ingestion (#1024). Its presence is a signal: runs
        /// recorded before it existed have no such key.
        /// </summary>
        public const string AccountsValidated = "accountsValidated";

        /// <summary>Matches stored by the run.</summary>
        public const string MatchesInserted = "matchesInserted";

        /// <summary>Match ids skipped because they were already stored.</summary>
        public const string MatchesSkipped = "matchesSkipped";

        /// <summary>Timelines fetched and stored by the run.</summary>
        public const string TimelinesUpdated = "timelinesUpdated";
    }

    /// <summary>Keys of <see cref="PipelineCatalog.MainAnalysis"/> runs.</summary>
    public static class MainAnalysis
    {
        /// <summary>Accounts demoted because they no longer qualify as a main.</summary>
        public const string DemotedAccounts = "demotedAccounts";
    }

    /// <summary>Keys of <see cref="PipelineCatalog.LadderSync"/> runs, read by its daily Riot ledger.</summary>
    public static class LadderSync
    {
        /// <summary>Whole-tier apex reads the run made.</summary>
        public const string ApexCalls = "apexCalls";

        /// <summary>Budgeted per-division pages the run made.</summary>
        public const string PagedCalls = "pagedCalls";
    }

    /// <summary>Keys of <see cref="PipelineCatalog.PaceSampling"/> runs, read by its daily Riot budget.</summary>
    public static class PaceSampling
    {
        /// <summary>Every Riot call the run made, failed ones included.</summary>
        public const string RiotCalls = "riotCalls";
    }
}
