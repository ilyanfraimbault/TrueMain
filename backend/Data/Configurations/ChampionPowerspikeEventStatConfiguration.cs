using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionPowerspikeEventStatConfiguration : IEntityTypeConfiguration<ChampionPowerspikeEventStat>
{
    public void Configure(EntityTypeBuilder<ChampionPowerspikeEventStat> entity)
    {
        entity.ToTable("champion_powerspike_event_stats");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.TeamPosition).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.EloBracket).IsRequired().HasMaxLength(20).HasColumnName("elo_bracket").HasDefaultValue(string.Empty);
        entity.Property(e => e.EventType).IsRequired().HasMaxLength(8);
        entity.Property(e => e.RefId).IsRequired();
        entity.Property(e => e.SumSpike).IsRequired();
        entity.Property(e => e.SumMinute).IsRequired();
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the aggregate grain and the ON CONFLICT target for the
        // incremental additive upsert.
        entity.HasIndex(e => new { e.ChampionId, e.TeamPosition, e.Patch, e.EloBracket, e.EventType, e.RefId })
            .IsUnique();
    }
}
