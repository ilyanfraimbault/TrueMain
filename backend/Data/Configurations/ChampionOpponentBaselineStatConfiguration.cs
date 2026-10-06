using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionOpponentBaselineStatConfiguration : IEntityTypeConfiguration<ChampionOpponentBaselineStat>
{
    public void Configure(EntityTypeBuilder<ChampionOpponentBaselineStat> entity)
    {
        entity.ToTable("champion_opponent_baseline_stats");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.TeamPosition).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Side).IsRequired().HasMaxLength(8);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.EloBracket).IsRequired().HasMaxLength(20);
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        entity.HasIndex(e => new { e.ChampionId, e.TeamPosition, e.Side, e.Patch, e.EloBracket })
            .IsUnique()
            .HasDatabaseName("IX_champion_opponent_baseline_stats_grain");
    }
}
