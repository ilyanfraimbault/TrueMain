using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ChampionAggregateStarterItemsConfiguration : IEntityTypeConfiguration<ChampionAggregateStarterItems>
{
    public void Configure(EntityTypeBuilder<ChampionAggregateStarterItems> entity)
    {
        entity.ToTable("champion_aggregate_starter_items");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ScopeId).IsRequired();
        entity.Property(e => e.StarterItemsKey).IsRequired().HasMaxLength(64);
        entity.Property(e => e.StarterItems).IsRequired().HasColumnType("jsonb");
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.Wins).IsRequired();

        entity.HasIndex(e => new { e.ScopeId, e.StarterItemsKey }).IsUnique();
        entity.HasIndex(e => e.ScopeId);

        entity.HasOne(e => e.Scope)
            .WithMany(s => s.StarterItems)
            .HasForeignKey(e => e.ScopeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
