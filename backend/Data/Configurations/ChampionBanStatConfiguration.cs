using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionBanStatConfiguration : IEntityTypeConfiguration<ChampionBanStat>
{
    public void Configure(EntityTypeBuilder<ChampionBanStat> entity)
    {
        entity.ToTable("champion_ban_stats");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.EloBracket).IsRequired().HasMaxLength(20).HasColumnName("elo_bracket").HasDefaultValue(string.Empty);
        entity.Property(e => e.Bans).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the grain + the ON CONFLICT target of the incremental
        // upsert. Patch and band lead the key because the read is always
        // "every champion's ban count for this patch at this band" — one range
        // scan per request rather than a seek per champion.
        entity.HasIndex(e => new
        {
            e.Patch,
            e.EloBracket,
            e.ChampionId,
        }).IsUnique().HasDatabaseName("IX_champion_ban_stats_grain");
    }
}
