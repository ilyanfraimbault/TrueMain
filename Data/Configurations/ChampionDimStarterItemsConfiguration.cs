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

        entity.Property(e => e.StarterItemsKey).IsRequired().HasMaxLength(64);
        entity.Property(e => e.StarterItems).IsRequired().HasColumnType("jsonb");

        entity.HasIndex(e => e.StarterItemsKey).IsUnique();
    }
}
