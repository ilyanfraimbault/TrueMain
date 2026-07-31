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
        entity.Property(e => e.BuildFirstItemId).IsRequired().HasDefaultValue(0);
        entity.Property(e => e.BuildKeystoneId).IsRequired().HasDefaultValue(0);
        entity.Property(e => e.OpponentChampionId).IsRequired().HasDefaultValue(0);
        entity.Property(e => e.EventType).IsRequired().HasMaxLength(8);
        entity.Property(e => e.RefId).IsRequired();
        entity.Property(e => e.SumSpike).IsRequired();
        entity.Property(e => e.SumMinute).IsRequired();
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the aggregate grain and the ON CONFLICT target for the
        // incremental additive upsert. The core-build pair sits before the event
        // columns so the index also serves the read, which always filters on a
        // single (champion, position, patch, elo, build) slice; the opponent sits
        // right after it for the same reason — the matchup read adds exactly that
        // one equality on top of the same prefix, and the unscoped read keeps
        // using the prefix that stops before it.
        entity.HasIndex(e => new
        {
            e.ChampionId,
            e.TeamPosition,
            e.Patch,
            e.EloBracket,
            e.BuildFirstItemId,
            e.BuildKeystoneId,
            e.OpponentChampionId,
            e.EventType,
            e.RefId
        }).IsUnique();
    }
}
