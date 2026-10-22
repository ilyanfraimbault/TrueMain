using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionBuildPurgedPatchConfiguration : IEntityTypeConfiguration<ChampionBuildPurgedPatch>
{
    public void Configure(EntityTypeBuilder<ChampionBuildPurgedPatch> entity)
    {
        entity.ToTable("champion_build_purged_patches");

        entity.HasKey(e => e.Patch);

        entity.Property(e => e.Patch).IsRequired().HasMaxLength(32);
        entity.Property(e => e.PurgedAtUtc).IsRequired();
    }
}
