using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionMatchupStatConfiguration : IEntityTypeConfiguration<ChampionMatchupStat>
{
    public void Configure(EntityTypeBuilder<ChampionMatchupStat> entity)
    {
        entity.ToTable("champion_matchup_stats");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.TeamPosition).IsRequired().HasMaxLength(16);
        entity.Property(e => e.OpponentChampionId).IsRequired();
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the aggregate grain. Its leading (ChampionId,
        // TeamPosition) prefix is also the read seek: the global matchups read
        // filters on those two then folds opponents to the requested patch scope.
        entity.HasIndex(e => new { e.ChampionId, e.TeamPosition, e.OpponentChampionId, e.Patch })
            .IsUnique();
    }
}
