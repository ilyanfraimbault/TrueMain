using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionSynergyBaselineStatConfiguration : IEntityTypeConfiguration<ChampionSynergyBaselineStat>
{
    public void Configure(EntityTypeBuilder<ChampionSynergyBaselineStat> entity)
    {
        entity.ToTable("champion_synergy_baseline_stats");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.TeamPosition).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Side).IsRequired().HasMaxLength(8);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.EloBracket).IsRequired().HasMaxLength(20).HasColumnName("elo_bracket").HasDefaultValue(string.Empty);
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the grain + the ON CONFLICT target of the incremental
        // upsert. Side leads the read seek because both reads scan a whole side:
        // the cohort intercept sums every SELF row in scope, and the partner
        // baselines are looked up as one ALLY set per request.
        entity.HasIndex(e => new
        {
            e.Side,
            e.ChampionId,
            e.TeamPosition,
            e.Patch,
            e.EloBracket,
        }).IsUnique().HasDatabaseName("IX_champion_synergy_baseline_stats_grain");
    }
}
