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

        entity.HasOne(e => e.Catalog)
            .WithMany()
            .HasForeignKey(e => e.PerkSelectionCatalogId)
            .OnDelete(DeleteBehavior.Restrict);

        // Hard FK to matches so a half-ingested match (mid-flow rollback)
        // cannot leave orphan perk selection rows that block re-ingestion
        // via the (MatchId, ParticipantId, PerkSelectionCatalogId) unique
        // index. Cascade so a Match delete also removes its perk rows.
        entity.HasOne<Match>()
            .WithMany()
            .HasForeignKey(e => e.MatchId)
            .HasPrincipalKey(m => m.Id)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(e => new { e.MatchId, e.ParticipantId, e.PerkSelectionCatalogId })
            .IsUnique();
    }
}
