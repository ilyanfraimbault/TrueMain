using Data.DataQuality;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionDimStarterItemsConfiguration : IEntityTypeConfiguration<ChampionDimStarterItems>
{
    public void Configure(EntityTypeBuilder<ChampionDimStarterItems> entity)
    {
        entity.ToTable("champion_dim_starter_items");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.StarterItems).IsRequired().HasColumnType("jsonb");

        // Identity, computed by Postgres from the basket itself (#1418). The writer
        // supplies only StarterItems; a basket stored in another order — or with the
        // key computed by a future writer that gets it wrong — lands on the same
        // generated value and is rejected by the index below.
        entity.Property(e => e.CanonicalKey)
            .IsRequired()
            .HasMaxLength(64)
            .HasComputedColumnSql(ChampionDimensionCanonicalKeys.StarterItemsCanonicalKeySql, stored: true);

        entity.HasIndex(e => e.CanonicalKey).IsUnique();
    }
}
