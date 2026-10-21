using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

/// <summary>The component order of #2183: one additive counter per (scope, item, recipe slot, rank).</summary>
public sealed class ChampionItemComponentOrderConfiguration : IEntityTypeConfiguration<ChampionItemComponentOrder>
{
    public void Configure(EntityTypeBuilder<ChampionItemComponentOrder> entity)
    {
        entity.ToTable("champion_item_component_orders");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedOnAdd();

        entity.Property(e => e.ChampionId).IsRequired();
        entity.Property(e => e.Position).IsRequired().HasMaxLength(16);
        entity.Property(e => e.Patch).IsRequired().HasMaxLength(16);
        entity.Property(e => e.ItemId).IsRequired();
        entity.Property(e => e.ComponentId).IsRequired();
        entity.Property(e => e.Occurrence).IsRequired();
        entity.Property(e => e.Rank).IsRequired();
        entity.Property(e => e.Games).IsRequired();
        entity.Property(e => e.AggregatedAtUtc).IsRequired();

        // The ON CONFLICT target of the additive upsert, and the read's access path: the API
        // loads one champion at one position over the few newest patches, so champion and
        // position lead and the patch window is one range scan.
        entity.HasIndex(e => new
        {
            e.ChampionId,
            e.Position,
            e.Patch,
            e.ItemId,
            e.ComponentId,
            e.Occurrence,
            e.Rank,
        }).IsUnique().HasDatabaseName("IX_champion_item_component_orders_grain");
    }
}
