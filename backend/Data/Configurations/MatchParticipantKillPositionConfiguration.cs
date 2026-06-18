using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class MatchParticipantKillPositionConfiguration : IEntityTypeConfiguration<MatchParticipantKillPosition>
{
    public void Configure(EntityTypeBuilder<MatchParticipantKillPosition> entity)
    {
        entity.ToTable("match_participant_kill_positions");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.MatchId)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.ParticipantId)
            .IsRequired();

        // Hard FK to matches so a half-ingested match cannot leave orphan rows, and
        // a Match delete cascades to its positions.
        entity.HasOne<Match>()
            .WithMany()
            .HasForeignKey(e => e.MatchId)
            .HasPrincipalKey(m => m.Id)
            .OnDelete(DeleteBehavior.Cascade);

        // The natural lookup + re-ingestion delete scope. Not unique: a participant
        // has several kill participations per match.
        entity.HasIndex(e => new { e.MatchId, e.ParticipantId });
    }
}
