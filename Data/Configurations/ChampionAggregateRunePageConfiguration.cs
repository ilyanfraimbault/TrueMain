using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionAggregateRunePageConfiguration : IEntityTypeConfiguration<ChampionAggregateRunePage>
{
    public void Configure(EntityTypeBuilder<ChampionAggregateRunePage> entity)
    {
        entity.ToTable("champion_aggregate_rune_pages");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ScopeId).IsRequired();
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
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();

        entity.HasIndex(e => new
        {
            e.ScopeId,
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
        entity.HasIndex(e => e.ScopeId);

        entity.HasOne(e => e.Scope)
            .WithMany(s => s.RunePages)
            .HasForeignKey(e => e.ScopeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
