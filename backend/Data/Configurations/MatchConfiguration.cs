using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> entity)
    {
        entity.ToTable("matches");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.Id)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.PlatformId)
            .IsRequired()
            .HasMaxLength(8);

        entity.Property(e => e.QueueId)
            .IsRequired();

        entity.Property(e => e.MapId)
            .IsRequired();

        entity.Property(e => e.GameMode)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.GameType)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.GameStartTimeUtc)
            .IsRequired();

        entity.Property(e => e.GameDurationSeconds)
            .IsRequired();

        entity.Property(e => e.GameVersion)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.CreatedAtUtc)
            .IsRequired()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("now()");

        entity.Property(e => e.TimelineIngested)
            .IsRequired()
            .HasDefaultValue(false);

        entity.Property(e => e.PowerspikeAggregated)
            .IsRequired()
            .HasDefaultValue(false);

        entity.Property(e => e.TimelineSnapshotsPruned)
            .IsRequired()
            .HasDefaultValue(false);

        entity.HasIndex(e => e.PlatformId);

        entity.HasIndex(e => new { e.PlatformId, e.QueueId, e.GameStartTimeUtc })
            .HasDatabaseName("IX_matches_platform_queue_game_start");

        entity.HasIndex(e => e.TimelineIngested)
            .HasDatabaseName("IX_matches_timeline_ingested");

        // Partial index over the not-yet-aggregated tail so the incremental
        // powerspike batch selection stays cheap; once backfilled almost every row
        // is aggregated, so the filtered index holds only the recent pending matches.
        entity.HasIndex(e => e.QueueId)
            .HasDatabaseName("IX_matches_powerspike_pending")
            .HasFilter("\"PowerspikeAggregated\" = false");

        // Partial index over the aggregated-but-not-yet-pruned tail so retention's
        // snapshot-pruning selection stays cheap; it empties as pruning catches up
        // and only ever holds the recently-aggregated matches awaiting a prune. The
        // named overload keeps this a distinct index from IX_matches_powerspike_pending
        // above (both key on QueueId, so EF would otherwise fold them into one).
        entity.HasIndex(e => e.QueueId, "IX_matches_snapshot_prune_pending")
            .HasFilter("\"PowerspikeAggregated\" = true AND \"TimelineSnapshotsPruned\" = false");

        entity.HasMany(e => e.Participants)
            .WithOne(e => e.Match)
            .HasForeignKey(e => e.MatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
