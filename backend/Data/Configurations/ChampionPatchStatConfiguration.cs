using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionPatchStatConfiguration : IEntityTypeConfiguration<ChampionPatchStat>
{
    public void Configure(EntityTypeBuilder<ChampionPatchStat> entity)
    {
        entity.ToTable("champion_patch_stats");

        // Natural-key PK on the whole grain, also the ON CONFLICT target of the fold. Queue
        // and patch lead because every reader asks for a set of patches of one queue — the
        // trend's last five, the purge's "does this patch have its record" (#1957, #1958).
        entity.HasKey(e => new { e.QueueId, e.Patch, e.Position, e.EloBracket, e.IsMain, e.ChampionId });

        entity.Property(e => e.QueueId).IsRequired();
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(32);
        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.Position).IsRequired().HasMaxLength(16);
        entity.Property(e => e.EloBracket).IsRequired().HasMaxLength(20);
        entity.Property(e => e.IsMain).IsRequired();
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();
    }
}
