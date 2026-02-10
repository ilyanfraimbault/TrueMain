using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class PerkSelectionCatalogConfiguration : IEntityTypeConfiguration<PerkSelectionCatalog>
{
    public void Configure(EntityTypeBuilder<PerkSelectionCatalog> entity)
    {
        entity.ToTable("perk_selection_catalog");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.StyleId)
            .IsRequired();

        entity.Property(e => e.SelectionIndex)
            .IsRequired();

        entity.Property(e => e.PerkId)
            .IsRequired();

        entity.Property(e => e.StyleDescription)
            .IsRequired()
            .HasMaxLength(16);

        entity.HasIndex(e => new { e.StyleId, e.SelectionIndex, e.PerkId, e.StyleDescription })
            .IsUnique();
    }
}
