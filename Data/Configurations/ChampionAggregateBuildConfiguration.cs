using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionAggregateBuildConfiguration : IEntityTypeConfiguration<ChampionAggregateBuild>
{
    public void Configure(EntityTypeBuilder<ChampionAggregateBuild> entity)
    {
        entity.ToTable("champion_aggregate_builds");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ScopeId).IsRequired();
        entity.Property(e => e.BootsItemId).IsRequired();
        entity.Property(e => e.BuildItem0).IsRequired();
        entity.Property(e => e.BuildItem1).IsRequired();
        entity.Property(e => e.BuildItem2).IsRequired();
        entity.Property(e => e.BuildItem3).IsRequired();
        entity.Property(e => e.BuildItem4).IsRequired();
        entity.Property(e => e.BuildItem5).IsRequired();
        entity.Property(e => e.BuildItem6).IsRequired();
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();

        entity.HasIndex(e => new
        {
            e.ScopeId,
            e.BootsItemId,
            e.BuildItem0,
            e.BuildItem1,
            e.BuildItem2,
            e.BuildItem3,
            e.BuildItem4,
            e.BuildItem5,
            e.BuildItem6
        }).IsUnique();
        entity.HasIndex(e => e.ScopeId);

        entity.HasOne(e => e.Scope)
            .WithMany(s => s.Builds)
            .HasForeignKey(e => e.ScopeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
