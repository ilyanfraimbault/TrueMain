using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionSynergyStatConfiguration : IEntityTypeConfiguration<ChampionSynergyStat>
{
    public void Configure(EntityTypeBuilder<ChampionSynergyStat> entity)
    {
        entity.ToTable("champion_synergy_stats");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.TeamPosition).IsRequired().HasMaxLength(16);
        entity.Property(e => e.PartnerChampionId).IsRequired();
        entity.Property(e => e.PartnerPosition).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.EloBracket).IsRequired().HasMaxLength(20).HasColumnName("elo_bracket").HasDefaultValue(string.Empty);
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the aggregate grain, and the read seek at the same time:
        // the synergies read filters on the (ChampionId, TeamPosition) prefix,
        // optionally narrows to one partner position, then folds partners to the
        // requested patch / elo scope. It is what the incremental upsert's
        // ON CONFLICT targets, so it must stay unique.
        entity.HasIndex(e => new
        {
            e.ChampionId,
            e.TeamPosition,
            e.PartnerChampionId,
            e.PartnerPosition,
            e.Patch,
            e.EloBracket,
        }).IsUnique().HasDatabaseName("IX_champion_synergy_stats_grain");
    }
}
