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
    public DbSet<ParticipantPerkSelection> ParticipantPerkSelections => Set<ParticipantPerkSelection>();
    public DbSet<PerkSelectionCatalog> PerkSelectionCatalogs => Set<PerkSelectionCatalog>();
    public DbSet<MainCandidate> MainCandidates => Set<MainCandidate>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MainChampionStat> MainChampionStats => Set<MainChampionStat>();
    public DbSet<ChampionAggregateScope> ChampionAggregateScopes => Set<ChampionAggregateScope>();

    // Phase 6: junction-table aggregate + globally-deduplicated dimension
    // tables. Phase 6.4 dropped the legacy ChampionPatternAggregate +
    // per-scope ChampionAggregate{Build,RunePage,SkillOrder,SpellPair,
    // StarterItems} tables; the aggregator writes patterns + dim rows
    // exclusively now and the read side projects them via
    // ChampionPatternProjector.
    public DbSet<ChampionAggregatePattern> ChampionAggregatePatterns => Set<ChampionAggregatePattern>();
    public DbSet<ChampionDimBuild> ChampionDimBuilds => Set<ChampionDimBuild>();
    public DbSet<ChampionDimRunePage> ChampionDimRunePages => Set<ChampionDimRunePage>();
    public DbSet<ChampionDimSkillOrder> ChampionDimSkillOrders => Set<ChampionDimSkillOrder>();
    public DbSet<ChampionDimSpellPair> ChampionDimSpellPairs => Set<ChampionDimSpellPair>();
    public DbSet<ChampionDimStarterItems> ChampionDimStarterItems => Set<ChampionDimStarterItems>();

    public DbSet<ProcessRun> ProcessRuns => Set<ProcessRun>();

    public DbSet<LogEntry> LogEntries => Set<LogEntry>();

    public DbSet<RankSnapshot> RankSnapshots => Set<RankSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrueMainDbContext).Assembly);
    }
}
