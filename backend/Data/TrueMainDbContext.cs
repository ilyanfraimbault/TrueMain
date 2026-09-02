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
    public DbSet<MatchBan> MatchBans => Set<MatchBan>();
    public DbSet<ParticipantPerkSelection> ParticipantPerkSelections => Set<ParticipantPerkSelection>();
    public DbSet<PerkSelectionCatalog> PerkSelectionCatalogs => Set<PerkSelectionCatalog>();
    public DbSet<MainCandidate> MainCandidates => Set<MainCandidate>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MainChampionStat> MainChampionStats => Set<MainChampionStat>();

    // Pre-aggregated read model for the champion page's global matchups
    // leaderboard (#606), carrying the lane counters and gold/XP gaps folded into
    // the same rows (#919/#976/#1111). The champion side is a main of that
    // champion — Data.Aggregation.ChampionCohort (#1087). Populated by
    // ChampionMatchupLeadAggregationProcess, lane counters by
    // ChampionLaneOutcomeAggregationProcess.
    public DbSet<ChampionMatchupStat> ChampionMatchupStats => Set<ChampionMatchupStat>();

    // Pre-aggregated same-team co-occurrence for the champion synergies panel
    // (#922) plus the marginal win rates its expected-win-rate model is measured
    // against. Both are populated by ChampionSynergyAggregationProcess in one
    // fold, so they always describe the same cohort of matches.
    public DbSet<ChampionSynergyStat> ChampionSynergyStats => Set<ChampionSynergyStat>();
    public DbSet<ChampionSynergyBaselineStat> ChampionSynergyBaselineStats => Set<ChampionSynergyBaselineStat>();

    // Champion ban counts and the match totals they are divided by (#920), both
    // populated by ChampionBanAggregationProcess in one fold so a ban rate is
    // always numerator and denominator over the same cohort of matches.
    public DbSet<ChampionBanStat> ChampionBanStats => Set<ChampionBanStat>();
    public DbSet<BanScopeTotal> BanScopeTotals => Set<BanScopeTotal>();

    // Pre-aggregated champion powerspikes (#694): the per-minute power curve, the
    // per-event slope-change spikes, and the global per-minute lead spread. Populated
    // incrementally by ChampionPowerspikeAggregationProcess so the dense per-minute
    // MatchParticipantTimelineSnapshot rows can be pruned to the canonical marks.
    public DbSet<ChampionPowerspikeCurveStat> ChampionPowerspikeCurveStats => Set<ChampionPowerspikeCurveStat>();
    public DbSet<ChampionPowerspikeEventStat> ChampionPowerspikeEventStats => Set<ChampionPowerspikeEventStat>();
    public DbSet<PowerspikeSigmaStat> PowerspikeSigmaStats => Set<PowerspikeSigmaStat>();

    public DbSet<ChampionAggregateScope> ChampionAggregateScopes => Set<ChampionAggregateScope>();

    // Junction-table aggregate + globally-deduplicated dimension tables: the
    // aggregator writes pattern + dim rows exclusively, and the read side joins
    // them in its own query services (Api/Services/Champions/ChampionBuildsQueryService
    // and friends). The single ChampionPatternProjector that used to own that join
    // is long gone.
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

    public DbSet<LadderSyncCursor> LadderSyncCursors => Set<LadderSyncCursor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrueMainDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // A single, explicit scale rule for the EF-mapped stat doubles (e.g.
        // MainChampionStat.PlayRate and the champion_powerspike_* rates) instead of
        // per-property guesswork. Npgsql maps double to `double precision` and
        // ignores the facet, so this carries no schema delta — it documents intent
        // and applies automatically to any future double property. (It does not
        // reach doubles nested inside jsonb columns such as PositionStat.Rate, which
        // are serialized as a blob rather than mapped as columns.)
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
