using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionGameCountConfiguration : IEntityTypeConfiguration<ChampionGameCount>
{
    public void Configure(EntityTypeBuilder<ChampionGameCount> entity)
    {
        entity.ToTable("champion_game_counts");

        // Natural-key PK on the whole grain. Queue, patch and platform lead because the
        // refresh replaces one (queue, patch, platform) slice at a time, so its DELETE is a
        // prefix range of this key.
        entity.HasKey(e => new { e.QueueId, e.Patch, e.PlatformId, e.TeamPosition, e.ChampionId });

        entity.Property(e => e.PlatformId).IsRequired().HasMaxLength(8);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.QueueId).IsRequired();
        entity.Property(e => e.TeamPosition).IsRequired().HasMaxLength(32);
        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.RefreshedAtUtc).IsRequired();
    }
}
