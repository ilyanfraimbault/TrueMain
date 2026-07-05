using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class JungleFirstClearConfiguration : IEntityTypeConfiguration<JungleFirstClear>
{
    public void Configure(EntityTypeBuilder<JungleFirstClear> entity)
    {
        entity.ToTable("jungle_first_clears");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.MatchId)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.ParticipantId)
            .IsRequired();

        // Compact ordered camp sequence, mirroring MatchParticipant.ItemEvents/SkillEvents.
        entity.Property(e => e.Steps)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(e => e.FullClearTimeMs);

        // Hard FK to matches so a half-ingested match cannot leave orphan first-clear
        // rows, and a Match delete cascades to its first clears.
        entity.HasOne<Match>()
            .WithMany()
            .HasForeignKey(e => e.MatchId)
            .HasPrincipalKey(m => m.Id)
            .OnDelete(DeleteBehavior.Cascade);

        // One first clear per jungler per match; also the dedup guard for timeline
        // re-ingestion.
        entity.HasIndex(e => new { e.MatchId, e.ParticipantId })
            .IsUnique();
    }
}
