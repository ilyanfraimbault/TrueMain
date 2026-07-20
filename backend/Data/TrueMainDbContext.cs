using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data;

public class TrueMainDbContext : DbContext
{
    public TrueMainDbContext(DbContextOptions<TrueMainDbContext> options) : base(options)
    {
    }

    public DbSet<RiotAccount> RiotAccounts => Set<RiotAccount>();
    public DbSet<Persona> Personas => Set<Persona>();
    public DbSet<MatchParticipant> MatchParticipants => Set<MatchParticipant>();
    public DbSet<MatchParticipantTimelineSnapshot> MatchParticipantTimelineSnapshots => Set<MatchParticipantTimelineSnapshot>();
    public DbSet<MatchParticipantKillPosition> MatchParticipantKillPositions => Set<MatchParticipantKillPosition>();
    public DbSet<JungleFirstClear> JungleFirstClears => Set<JungleFirstClear>();
    public DbSet<ParticipantPerkSelection> ParticipantPerkSelections => Set<ParticipantPerkSelection>();
    public DbSet<PerkSelectionCatalog> PerkSelectionCatalogs => Set<PerkSelectionCatalog>();
    public DbSet<MainCandidate> MainCandidates => Set<MainCandidate>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MainChampionStat> MainChampionStats => Set<MainChampionStat>();

    // Pre-aggregated read models for the champion page's two heaviest slices
    // (#606): the global matchups leaderboard and the lead-vs-lane-opponent
    // timeline curve. Populated by ChampionMatchupLeadAggregationProcess.
    public DbSet<ChampionMatchupStat> ChampionMatchupStats => Set<ChampionMatchupStat>();
    public DbSet<ChampionTimelineLeadStat> ChampionTimelineLeadStats => Set<ChampionTimelineLeadStat>();

    // Pre-aggregated champion powerspikes (#694): the per-minute power curve, the
    // per-event slope-change spikes, and the global per-minute lead spread. Populated
    // incrementally by ChampionPowerspikeAggregationProcess so the dense per-minute
    // MatchParticipantTimelineSnapshot rows can be pruned to the canonical marks.
    public DbSet<ChampionPowerspikeCurveStat> ChampionPowerspikeCurveStats => Set<ChampionPowerspikeCurveStat>();
    public DbSet<ChampionPowerspikeEventStat> ChampionPowerspikeEventStats => Set<ChampionPowerspikeEventStat>();
    public DbSet<PowerspikeSigmaStat> PowerspikeSigmaStats => Set<PowerspikeSigmaStat>();

    public DbSet<ChampionAggregateScope> ChampionAggregateScopes => Set<ChampionAggregateScope>();

    // Junction-table aggregate + globally-deduplicated dimension tables:
    // the aggregator writes pattern + dim rows exclusively, and the read
    // side projects them via ChampionPatternProjector.
    public DbSet<ChampionAggregatePattern> ChampionAggregatePatterns => Set<ChampionAggregatePattern>();
    public DbSet<ChampionDimBuild> ChampionDimBuilds => Set<ChampionDimBuild>();
    public DbSet<ChampionDimRunePage> ChampionDimRunePages => Set<ChampionDimRunePage>();
    public DbSet<ChampionDimSkillOrder> ChampionDimSkillOrders => Set<ChampionDimSkillOrder>();
    public DbSet<ChampionDimSpellPair> ChampionDimSpellPairs => Set<ChampionDimSpellPair>();
    public DbSet<ChampionDimStarterItems> ChampionDimStarterItems => Set<ChampionDimStarterItems>();

    public DbSet<ProcessRun> ProcessRuns => Set<ProcessRun>();

    public DbSet<RankSnapshot> RankSnapshots => Set<RankSnapshot>();

    public DbSet<SeedRequest> SeedRequests => Set<SeedRequest>();

    public DbSet<DiscoveryCursor> DiscoveryCursors => Set<DiscoveryCursor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrueMainDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // A single, explicit scale rule for the rate-style doubles
        // (MainChampionStat.PlayRate, PositionStat.Rate) instead of per-property
        // guesswork. Npgsql maps double to `double precision` and ignores the
        // facet, so this carries no schema delta — it documents intent and applies
        // automatically to any future double property.
        //
        // A matching Properties<DateTime>().HavePrecision(6) was deliberately left
        // out: our timestamps are already `timestamp with time zone`, which stores
        // microseconds (precision 6) natively, so the facet changes nothing at
        // runtime — but EF still scaffolds an ALTER COLUMN TYPE for every timestamp
        // column (~30, across matches/rank_snapshots and other populated tables),
        // and Postgres does not guarantee a metadata-only path for a timestamp
        // typmod change. That is a schema-wide rewrite risk for zero functional
        // gain, which the "keep migrations fast" rule rules out. See issue #228.
        configurationBuilder.Properties<double>().HavePrecision(18, 6);
    }
}
