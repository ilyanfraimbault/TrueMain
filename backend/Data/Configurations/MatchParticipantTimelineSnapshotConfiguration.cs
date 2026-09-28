using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class MatchParticipantTimelineSnapshotConfiguration : IEntityTypeConfiguration<MatchParticipantTimelineSnapshot>
{
    public void Configure(EntityTypeBuilder<MatchParticipantTimelineSnapshot> entity)
    {
        entity.ToTable("match_participant_timeline_snapshots");

        // One snapshot per participant per interval: the natural key is the primary
        // key — the lookup key and the dedup guard for timeline re-ingestion (#1697).
        entity.HasKey(e => new { e.MatchId, e.ParticipantId, e.IntervalMinute });

        entity.Property(e => e.MatchId)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.ParticipantId)
            .IsRequired();

        entity.Property(e => e.IntervalMinute)
            .IsRequired();

        // Hard FK to matches so a half-ingested match cannot leave orphan
        // snapshot rows, and a Match delete cascades to its snapshots.
        entity.HasOne<Match>()
            .WithMany()
            .HasForeignKey(e => e.MatchId)
            .HasPrincipalKey(m => m.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
