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
        entity.Property(e => e.EventType).IsRequired().HasMaxLength(8);
        entity.Property(e => e.RefId).IsRequired();
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.SumEventMinute).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the aggregate grain; also the ON CONFLICT target of the
        // incremental upsert and the read seek (fold patches within champion+pos).
        entity.HasIndex(e => new { e.ChampionId, e.TeamPosition, e.Patch, e.EventType, e.RefId })
            .IsUnique();
    }
}
