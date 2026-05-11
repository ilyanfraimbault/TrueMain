using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionDimBuildConfiguration : IEntityTypeConfiguration<ChampionDimBuild>
{
    public void Configure(EntityTypeBuilder<ChampionDimBuild> entity)
    {
        entity.ToTable("champion_dim_builds");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.BootsItemId).IsRequired();
        entity.Property(e => e.BuildItem0).IsRequired();
        entity.Property(e => e.BuildItem1).IsRequired();
        entity.Property(e => e.BuildItem2).IsRequired();
        entity.Property(e => e.BuildItem3).IsRequired();
        entity.Property(e => e.BuildItem4).IsRequired();
        entity.Property(e => e.BuildItem5).IsRequired();
        entity.Property(e => e.BuildItem6).IsRequired();

        // UNIQUE on the full content tuple is what makes this a deduplicated
        // reference: the get-or-create path can use INSERT ... ON CONFLICT
        // DO NOTHING + a follow-up SELECT to reach the row, regardless of
        // who inserted it.
        entity.HasIndex(e => new
        {
            e.BootsItemId,
            e.BuildItem0,
            e.BuildItem1,
            e.BuildItem2,
            e.BuildItem3,
            e.BuildItem4,
            e.BuildItem5,
            e.BuildItem6
        }).IsUnique();
    }
}
