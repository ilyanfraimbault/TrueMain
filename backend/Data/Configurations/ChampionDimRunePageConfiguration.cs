using Data.DataQuality;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionDimRunePageConfiguration : IEntityTypeConfiguration<ChampionDimRunePage>
{
    public void Configure(EntityTypeBuilder<ChampionDimRunePage> entity)
    {
        // The two secondary perks are a set, so the identity is the page with that pair
        // sorted — not the eleven columns as stored, which is what let one page exist as
        // two rows for 48% of the dimension (#911). The UNIQUE index over the canonical
        // expression lives in the migration (EF cannot model an expression index); the
        // CHECK keeps the stored pair sorted so the reader's lookup and the index agree.
        entity.ToTable(
            "champion_dim_rune_pages",
            table => table.HasCheckConstraint(
                ChampionDimensionCanonicalKeys.RunePageCanonicalCheckName,
                ChampionDimensionCanonicalKeys.RunePageCanonicalCheck));

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

        // Kept non-unique: the canonical UNIQUE index cannot serve an equality lookup on
        // the raw columns, and the resolver reads the dimension by keystone.
        entity.HasIndex(e => e.PrimaryKeystoneId);
    }
}
