using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class MatchParticipantTimelineSnapshotConfiguration : IEntityTypeConfiguration<MatchParticipantTimelineSnapshot>
{
    public void Configure(EntityTypeBuilder<MatchParticipantTimelineSnapshot> entity)
    {
        entity.ToTable("match_participant_timeline_snapshots");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

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

        // One snapshot per participant per interval: the dedup guard for timeline
        // re-ingestion and the lookup key for the champion timeline-leads read,
        // which joins this table twice (champion + lane opponent) by
        // (MatchId, ParticipantId, IntervalMinute). INCLUDE the measured columns
        // so those joins are index-only scans instead of one random heap fetch
        // per matched row — the heap traffic that made the read time out at scale
        // once parallel query was disabled (#594). Uniqueness still keys on the
        // three columns only; the included payload is not part of the key.
        entity.HasIndex(e => new { e.MatchId, e.ParticipantId, e.IntervalMinute })
            .IsUnique()
            .IncludeProperties(e => new
            {
                e.TotalGold,
                e.MinionsKilled,
                e.JungleMinionsKilled,
                e.Kills,
                e.Level,
                e.Xp,
                e.DamageToChampions
            });
    }
}
