using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionDamageProfileStatConfiguration : IEntityTypeConfiguration<ChampionDamageProfileStat>
{
    public void Configure(EntityTypeBuilder<ChampionDamageProfileStat> entity)
    {
        entity.ToTable("champion_damage_profile_stats");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.Position).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);

        // Text, not an int: a dimension read in ad-hoc SQL ("which archetypes does Kai'Sa
        // have"), and the upsert writes it by name (decisions/backend-conventions.md).
        entity.Property(e => e.Archetype).IsRequired().HasMaxLength(32).HasConversion<string>();

        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.PhysicalDamageToChampionsSum).IsRequired();
        entity.Property(e => e.MagicDamageToChampionsSum).IsRequired();
        entity.Property(e => e.TrueDamageToChampionsSum).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // Natural key on the grain + the ON CONFLICT target of the incremental upsert.
        // Patch leads for the same reason as on champion_profile_stats: the read takes a
        // whole patch window at once.
        entity.HasIndex(e => new
        {
            e.Patch,
            e.ChampionId,
            e.Position,
            e.Archetype,
        }).IsUnique().HasDatabaseName("IX_champion_damage_profile_stats_grain");
    }
}
