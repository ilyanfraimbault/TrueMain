using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class ParticipantPerkSelectionConfiguration : IEntityTypeConfiguration<ParticipantPerkSelection>
{
    public void Configure(EntityTypeBuilder<ParticipantPerkSelection> entity)
    {
        entity.ToTable("participant_perk_selections");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.MatchId)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.ParticipantId)
            .IsRequired();

        entity.Property(e => e.PerkSelectionCatalogId)
            .IsRequired();

        entity.HasOne<PerkSelectionCatalog>()
            .WithMany()
            .HasForeignKey(e => e.PerkSelectionCatalogId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(e => new { e.MatchId, e.ParticipantId, e.PerkSelectionCatalogId })
            .IsUnique();
    }
}
