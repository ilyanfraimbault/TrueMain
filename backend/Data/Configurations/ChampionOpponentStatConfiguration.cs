using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionOpponentStatConfiguration : IEntityTypeConfiguration<ChampionOpponentStat>
{
    public void Configure(EntityTypeBuilder<ChampionOpponentStat> entity)
    {
        entity.ToTable("champion_opponent_stats");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.TeamPosition).IsRequired().HasMaxLength(16);
        entity.Property(e => e.OpponentChampionId).IsRequired();
        entity.Property(e => e.OpponentPosition).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.EloBracket).IsRequired().HasMaxLength(20);
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // The aggregate grain, the upsert's ON CONFLICT target, and the draft read's
        // seek at once: candidates at our lane, against the enemies on the board.
        entity.HasIndex(e => new
        {
            e.ChampionId,
            e.TeamPosition,
            e.OpponentChampionId,
            e.OpponentPosition,
            e.Patch,
            e.EloBracket,
        }).IsUnique().HasDatabaseName("IX_champion_opponent_stats_grain");
    }
}
