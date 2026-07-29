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

        entity.Property(e => e.MatchupLeadAggregated)
            .IsRequired()
            .HasDefaultValue(false);

        entity.Property(e => e.SynergyAggregated)
            .IsRequired()
            .HasDefaultValue(false);

        entity.Property(e => e.BansAggregated)
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

        // Partial index over the not-yet-aggregated tail so the incremental
        // matchup/lead batch selection stays cheap, mirroring IX_matches_powerspike_pending.
        entity.HasIndex(e => e.QueueId, "IX_matches_matchup_lead_pending")
            .HasFilter("\"MatchupLeadAggregated\" = false");

        // Same shape for the synergy fold (#922). It starts out covering every
        // retained match (the flag ships false everywhere, on purpose) and shrinks
        // to the recent pending tail once the initial backfill has drained.
        entity.HasIndex(e => e.QueueId, "IX_matches_synergy_pending")
            .HasFilter("\"SynergyAggregated\" = false");

        // Same shape for the ban fold (#920). Unlike the synergy one this index
        // starts out empty — the flag is backfilled to true everywhere, since
        // pre-#920 matches carry no bans — and only ever holds the freshly-ingested
        // tail awaiting its fold.
        entity.HasIndex(e => e.QueueId, "IX_matches_bans_pending")
            .HasFilter("\"BansAggregated\" = false");

        entity.HasMany(e => e.Participants)
            .WithOne(e => e.Match)
            .HasForeignKey(e => e.MatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
