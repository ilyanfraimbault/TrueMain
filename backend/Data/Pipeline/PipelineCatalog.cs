using System.Collections.Frozen;

namespace Data.Pipeline;

/// <summary>
/// The ingestion pipeline: every step, its lane and its order. The ingestor runs and registers
/// its processes from it, the Api names them from it, and the admin draws it from the Api.
/// </summary>
public static class PipelineCatalog
{
    public static PipelineProcess LadderSync { get; } = new()
    {
        Mode = JobMode.LadderSyncOnly,
        Name = "LadderSync",
        Lane = PipelineLane.Fetch,
        Label = "Ladder Sync",
        Description = "Refreshes the rank of accounts we already track by reading the ladders themselves: the three apex tiers whole every run, then the tiers below Master page by page under a request budget."
    };

    public static PipelineProcess Discovery { get; } = new()
    {
        Mode = JobMode.DiscoveryOnly,
        Name = "Discovery",
        Lane = PipelineLane.Fetch,
        Label = "Discovery",
        Description = "Walks the Master/Grandmaster/Challenger ladders to find accounts we do not track yet, and derives candidates from their champion mastery."
    };

    public static PipelineProcess ManualSeed { get; } = new()
    {
        Mode = JobMode.ManualSeedOnly,
        Name = "ManualSeed",
        Lane = PipelineLane.Fetch,
        Label = "Manual Seed",
        Description = "Drains the seed requests added by hand from the admin, queueing them directly instead of making them compete for a top-N slot.",
        ReservedRiotShare = 0.02
    };

    public static PipelineProcess Harvest { get; } = new()
    {
        Mode = JobMode.HarvestOnly,
        Name = "Harvest",
        Lane = PipelineLane.Fetch,
        Label = "Harvest",
        Description = "Turns players already seen in ingested matches into candidates, at no Riot API cost."
    };

    public static PipelineProcess Scoring { get; } = new()
    {
        Mode = JobMode.ScoringOnly,
        Name = "Scoring",
        Lane = PipelineLane.Fetch,
        Label = "Scoring",
        Description = "Ranks candidates on recency, rank, mastery and champion scarcity, and promotes the per-platform top N to the ingestion queue.",
        RunsAfter = [JobMode.DiscoveryOnly, JobMode.HarvestOnly]
    };

    public static PipelineProcess MainActivity { get; } = new()
    {
        Mode = JobMode.MainActivityOnly,
        Name = "MainActivity",
        Lane = PipelineLane.Fetch,
        Label = "Main Activity",
        Description = "Retires mains who stopped playing and reactivates those who came back, judged on champion-mastery last-play time. Flags rows, never deletes them."
    };

    public static PipelineProcess MatchIngestion { get; } = new()
    {
        Mode = JobMode.MatchIngestionOnly,
        Name = "MatchIngestion",
        Lane = PipelineLane.Fetch,
        Label = "Match Ingestion",
        Description = "Claims queued accounts and fetches their matches and timelines — the raw rows every aggregation below reads.",
        // MainActivity first, so the claim does not spend match-v5 on mains it is about to retire (#900).
        RunsAfter = [JobMode.ManualSeedOnly, JobMode.ScoringOnly, JobMode.MainActivityOnly]
    };

    public static PipelineProcess MainAnalysis { get; } = new()
    {
        Mode = JobMode.MainAnalysisOnly,
        Name = "MainAnalysis",
        Lane = PipelineLane.Aggregate,
        Label = "Main Analysis",
        Description = "Decides who is a true main of which champion, from play rate against adaptive thresholds, and demotes those who no longer qualify."
    };

    public static PipelineProcess EloBracketEnrichment { get; } = new()
    {
        Mode = JobMode.EloBracketEnrichmentOnly,
        Name = "MatchParticipantEloBracketEnrichment",
        Lane = PipelineLane.Aggregate,
        Label = "Elo Bracketing",
        Description = "Stamps each game with the rank its players were at when they played it, so every stat below can be filtered by elo.",
        RunsAfter = [JobMode.LadderSyncOnly]
    };

    public static PipelineProcess PatternAggregation { get; } = new()
    {
        Mode = JobMode.PatternAggregationOnly,
        Name = "ChampionPatternAggregation",
        Lane = PipelineLane.Aggregate,
        Label = "Builds & Runes",
        Description = "Rebuilds the per-champion aggregates behind the build, rune, skill-order and summoner-spell panels.",
        IsMatchFold = true
    };

    public static PipelineProcess ProfileAggregation { get; } = new()
    {
        Mode = JobMode.ChampionProfileAggregationOnly,
        Name = "ChampionProfileAggregation",
        Lane = PipelineLane.Aggregate,
        Label = "Champion Profiles",
        Description = "Measures what each champion does in its games — damage split, healing, crowd control, damage taken, lane leads, item archetypes — the profiles that later qualify a draft as AP-heavy, tanky or sustain-heavy.",
        IsMatchFold = true
    };

    public static PipelineProcess ItemContextAggregation { get; } = new()
    {
        Mode = JobMode.ChampionItemContextAggregationOnly,
        Name = "ChampionItemContextAggregation",
        Lane = PipelineLane.Aggregate,
        Label = "Item Context",
        Description = "Counts how often each item is built in games against each kind of draft, then works out which situations actually move that choice — the \"why this item\" behind the build tree.",
        IsMatchFold = true,
        RunsAfter = [JobMode.ChampionProfileAggregationOnly]
    };

    public static PipelineProcess MatchupAggregation { get; } = new()
    {
        Mode = JobMode.MatchupLeadAggregationOnly,
        Name = "ChampionMatchupLeadAggregation",
        Lane = PipelineLane.Aggregate,
        Label = "Matchups",
        Description = "Folds each match into the champion-versus-champion matchup stats — who won the game, and who won lane at 15 minutes — counting only games where the champion side is one of its mains.",
        IsMatchFold = true,
        RunsAfter = [JobMode.EloBracketEnrichmentOnly]
    };

    public static PipelineProcess SynergyAggregation { get; } = new()
    {
        Mode = JobMode.SynergyAggregationOnly,
        Name = "ChampionSynergyAggregation",
        Lane = PipelineLane.Aggregate,
        Label = "Synergies",
        Description = "Folds each match into the same-team champion pair stats behind the synergy panel.",
        IsMatchFold = true,
        RunsAfter = [JobMode.EloBracketEnrichmentOnly]
    };

    public static PipelineProcess OpponentAggregation { get; } = new()
    {
        Mode = JobMode.OpponentAggregationOnly,
        Name = "ChampionOpponentAggregation",
        Lane = PipelineLane.Aggregate,
        Label = "Opponents",
        Description = "Folds each match into the champion-versus-enemy pair stats behind the draft's enemy-team component.",
        IsMatchFold = true,
        RunsAfter = [JobMode.EloBracketEnrichmentOnly]
    };

    public static PipelineProcess ScalingAggregation { get; } = new()
    {
        Mode = JobMode.ScalingAggregationOnly,
        Name = "ChampionScalingAggregation",
        Lane = PipelineLane.Aggregate,
        Label = "Scaling",
        Description = "Folds each match into the champion win-rate-by-game-length stats behind the scaling chart, counting only games where the champion side is one of its mains.",
        IsMatchFold = true,
        RunsAfter = [JobMode.EloBracketEnrichmentOnly]
    };

    public static PipelineProcess BanAggregation { get; } = new()
    {
        Mode = JobMode.BanAggregationOnly,
        Name = "ChampionBanAggregation",
        Lane = PipelineLane.Aggregate,
        Label = "Bans",
        Description = "Counts champion-select bans and the match totals they are divided by, to produce ban rates.",
        IsMatchFold = true,
        RunsAfter = [JobMode.EloBracketEnrichmentOnly]
    };

    public static PipelineProcess AccountRefresh { get; } = new()
    {
        Mode = JobMode.AccountRefreshOnly,
        Name = "AccountRefresh",
        Lane = PipelineLane.Fetch,
        Label = "Account Refresh",
        Description = "Refreshes Riot ID and rank one account at a time, and recovers or invalidates accounts whose PUUID stopped resolving. The fallback behind Ladder Sync.",
        ReservedRiotShare = 0.05
    };

    public static PipelineProcess PaceSampling { get; } = new()
    {
        Mode = JobMode.PaceSamplingOnly,
        Name = "PaceSampling",
        Lane = PipelineLane.Fetch,
        Label = "Pace Sampling",
        Description = "Reads a few games of the tiers TrueMain does not ingest (Iron to Platinum) into the desktop overlay's pace benchmark, under its own daily call cap, and keeps none of them — only the counters and the ids already read.",
        // Last of the Riot spenders, so it only uses what they left.
        RunsAfter = [JobMode.MatchIngestionOnly, JobMode.AccountRefreshOnly]
    };

    public static PipelineProcess MatchDataRetention { get; } = new()
    {
        Mode = JobMode.MatchDataRetentionOnly,
        Name = "MatchDataRetention",
        Lane = PipelineLane.Aggregate,
        Label = "Retention",
        Description = "Deletes what the site no longer serves — stale candidates, out-of-window matches, intermediate timeline snapshots — to keep the database from growing without bound."
    };

    public static PipelineProcess GameCountRefresh { get; } = new()
    {
        Mode = JobMode.ChampionGameCountRefreshOnly,
        Name = "ChampionGameCountRefresh",
        Lane = PipelineLane.Aggregate,
        Label = "Champion Game Counts",
        Description = "Recounts the stored games per champion, region, patch and lane from the raw matches, so the Champions page and the Overview read a small table instead of scanning every participant. At most every half hour, after retention, so a deleted patch stops counting.",
        RunsAfter = [JobMode.MatchDataRetentionOnly]
    };

    public static PipelineProcess CandidateStockSnapshot { get; } = new()
    {
        Mode = JobMode.CandidateStockSnapshotOnly,
        Name = "CandidateStockSnapshot",
        Lane = PipelineLane.Aggregate,
        Label = "Candidate Stock",
        Description = "Counts how many candidates sit in each stage of the funnel and records the reading, so the backlog can be charted over time. Runs after retention, so the level it stores is the one the pipeline actually sits at.",
        RunsAfter = [JobMode.MatchDataRetentionOnly]
    };

    public static PipelineProcess StorageSnapshot { get; } = new()
    {
        Mode = JobMode.StorageSnapshotOnly,
        Name = "StorageSnapshot",
        Lane = PipelineLane.Aggregate,
        Label = "Storage Snapshot",
        Description = "Records the day's database size per table, feeding the disk-usage forecast. Runs last so it measures the size after retention, not before.",
        RunsAfter = [JobMode.MatchDataRetentionOnly, JobMode.CandidateStockSnapshotOnly]
    };

    /// <summary>Every step, in the order <see cref="JobMode.Full"/> runs them.</summary>
    public static IReadOnlyList<PipelineProcess> Steps { get; } = Array.AsReadOnly(
    [
        LadderSync,
        Discovery,
        ManualSeed,
        Harvest,
        Scoring,
        MainActivity,
        MatchIngestion,
        MainAnalysis,
        EloBracketEnrichment,
        PatternAggregation,
        ProfileAggregation,
        ItemContextAggregation,
        MatchupAggregation,
        SynergyAggregation,
        OpponentAggregation,
        ScalingAggregation,
        BanAggregation,
        AccountRefresh,
        PaceSampling,
        MatchDataRetention,
        GameCountRefresh,
        CandidateStockSnapshot,
        StorageSnapshot
    ]);

    public static IReadOnlyList<PipelineLaneDescriptor> Lanes { get; } = Array.AsReadOnly(
    [
        new PipelineLaneDescriptor(
            PipelineLane.Fetch,
            "fetch",
            "Fetch lane",
            "Everything that talks to Riot, paced by the per-region limiter: it reads the ladders, picks who to visit and downloads their matches."),
        new PipelineLaneDescriptor(
            PipelineLane.Aggregate,
            "aggregate",
            "Aggregate lane",
            "Everything that only reads and writes Postgres: it folds the matches already downloaded into the stats the site serves, then prunes what is no longer needed.")
    ]);

    /// <summary>The modes that expand to several steps; nothing is registered under them.</summary>
    public static IReadOnlyList<JobMode> CompositeModes { get; } =
        Array.AsReadOnly([JobMode.Full, JobMode.FetchLane, JobMode.AggregateLane, JobMode.FetchWorkers]);

    /// <summary>
    /// The step <see cref="JobMode.FetchWorkers"/> runs as one permanent worker per regional
    /// route instead of inside its sequenced pass (#1457).
    /// </summary>
    public static PipelineProcess PermanentFetchWorkerStep => MatchIngestion;

    private static readonly FrozenDictionary<JobMode, PipelineProcess> ByMode =
        Steps.ToFrozenDictionary(step => step.Mode);

    private static readonly FrozenDictionary<string, PipelineProcess> ByName =
        Steps.ToFrozenDictionary(step => step.Name, StringComparer.Ordinal);

    private static readonly IReadOnlyList<PipelineProcess> FetchLaneSteps = InLane(PipelineLane.Fetch);

    private static readonly IReadOnlyList<PipelineProcess> AggregateLaneSteps = InLane(PipelineLane.Aggregate);

    private static readonly IReadOnlyList<PipelineProcess> FetchMaintenanceSteps =
        Array.AsReadOnly(FetchLaneSteps.Where(step => step != PermanentFetchWorkerStep).ToArray());

    /// <summary>The step a single-process mode runs.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A composite or undeclared mode.</exception>
    public static PipelineProcess Get(JobMode mode)
        => ByMode.TryGetValue(mode, out var step)
            ? step
            : throw new ArgumentOutOfRangeException(nameof(mode), mode, $"{mode} is not a single-process {nameof(JobMode)}.");

    /// <summary>The step recorded under <paramref name="name"/>, if any.</summary>
    public static PipelineProcess? Find(string? name)
        => name is not null && ByName.TryGetValue(name, out var step) ? step : null;

    /// <summary>The steps of one lane, in pipeline order.</summary>
    public static IReadOnlyList<PipelineProcess> InLane(PipelineLane lane)
        => Array.AsReadOnly(Steps.Where(step => step.Lane == lane).ToArray());

    /// <summary>
    /// The steps whose runs and claims a container running <paramref name="mode"/> owns: what
    /// its boot reconciles and releases.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">An undeclared mode.</exception>
    public static IReadOnlyList<PipelineProcess> StepsOwnedBy(JobMode mode) => mode switch
    {
        JobMode.Full => Steps,
        JobMode.FetchLane or JobMode.FetchWorkers => FetchLaneSteps,
        JobMode.AggregateLane => AggregateLaneSteps,
        _ => [Get(mode)]
    };

    /// <summary>
    /// The steps one pass of <paramref name="mode"/> runs in sequence: what it owns, less the
    /// step permanent fetch workers run on their own.
    /// </summary>
    public static IReadOnlyList<PipelineProcess> SequencedStepsFor(JobMode mode)
        => RunsPermanentFetchWorkers(mode) ? FetchMaintenanceSteps : StepsOwnedBy(mode);

    /// <summary>Whether <paramref name="mode"/> runs match fetching as permanent workers (#1457).</summary>
    public static bool RunsPermanentFetchWorkers(JobMode mode) => mode == JobMode.FetchWorkers;

    /// <summary>The composite modes whose passes run steps of <paramref name="lane"/>.</summary>
    public static IReadOnlyList<JobMode> ModesRunning(PipelineLane lane)
        => CompositeModes.Where(mode => StepsOwnedBy(mode).Any(step => step.Lane == lane)).ToArray();
}
