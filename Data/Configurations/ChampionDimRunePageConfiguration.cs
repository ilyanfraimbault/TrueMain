using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionDimRunePageConfiguration : IEntityTypeConfiguration<ChampionDimRunePage>
{
    public void Configure(EntityTypeBuilder<ChampionDimRunePage> entity)
    {
        entity.ToTable("champion_dim_rune_pages");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.PrimaryStyleId).IsRequired();
        entity.Property(e => e.PrimaryKeystoneId).IsRequired();
        entity.Property(e => e.PrimaryPerk1Id).IsRequired();
        entity.Property(e => e.PrimaryPerk2Id).IsRequired();
        entity.Property(e => e.PrimaryPerk3Id).IsRequired();
        entity.Property(e => e.SecondaryStyleId).IsRequired();
        entity.Property(e => e.SecondaryPerk1Id).IsRequired();
        entity.Property(e => e.SecondaryPerk2Id).IsRequired();
        entity.Property(e => e.StatOffense).IsRequired();
        entity.Property(e => e.StatFlex).IsRequired();
        entity.Property(e => e.StatDefense).IsRequired();

        entity.HasIndex(e => new
        {
            e.PrimaryStyleId,
            e.PrimaryKeystoneId,
            e.PrimaryPerk1Id,
            e.PrimaryPerk2Id,
            e.PrimaryPerk3Id,
            e.SecondaryStyleId,
            e.SecondaryPerk1Id,
            e.SecondaryPerk2Id,
            e.StatOffense,
            e.StatFlex,
            e.StatDefense
        }).IsUnique();
    }
}
