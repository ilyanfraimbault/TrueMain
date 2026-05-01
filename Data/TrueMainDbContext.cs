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
    public DbSet<ChampionPatternAggregate> ChampionPatternAggregates => Set<ChampionPatternAggregate>();
    public DbSet<ChampionAggregateScope> ChampionAggregateScopes => Set<ChampionAggregateScope>();
    public DbSet<ChampionAggregateSpellPair> ChampionAggregateSpellPairs => Set<ChampionAggregateSpellPair>();
    public DbSet<ChampionAggregateSkillOrder> ChampionAggregateSkillOrders => Set<ChampionAggregateSkillOrder>();
    public DbSet<ChampionAggregateStarterItems> ChampionAggregateStarterItems => Set<ChampionAggregateStarterItems>();
    public DbSet<ChampionAggregateBuild> ChampionAggregateBuilds => Set<ChampionAggregateBuild>();
    public DbSet<ChampionAggregateRunePage> ChampionAggregateRunePages => Set<ChampionAggregateRunePage>();

    // Phase 6: junction-table aggregate + globally-deduplicated dimension
    // tables. Empty until PR 6.2 wires the aggregator dual-write; the
    // legacy ChampionAggregate* tables above stay the source of truth for
    // reads until PR 6.3 swaps them out.
    public DbSet<ChampionAggregatePattern> ChampionAggregatePatterns => Set<ChampionAggregatePattern>();
    public DbSet<ChampionDimBuild> ChampionDimBuilds => Set<ChampionDimBuild>();
    public DbSet<ChampionDimRunePage> ChampionDimRunePages => Set<ChampionDimRunePage>();
    public DbSet<ChampionDimSkillOrder> ChampionDimSkillOrders => Set<ChampionDimSkillOrder>();
    public DbSet<ChampionDimSpellPair> ChampionDimSpellPairs => Set<ChampionDimSpellPair>();
    public DbSet<ChampionDimStarterItems> ChampionDimStarterItems => Set<ChampionDimStarterItems>();

    public DbSet<ProcessRun> ProcessRuns => Set<ProcessRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrueMainDbContext).Assembly);
    }
}
