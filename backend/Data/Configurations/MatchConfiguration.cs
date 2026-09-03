using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    /// <summary>
    /// The Postgres expression behind the stored generated column <c>matches."Patch"</c>
    /// (#1368). It is the SQL transcription of
    /// <c>PatchVersion.TryParse(gameVersion, out var v) ? v.ToMajorMinor() : null</c>:
    /// <list type="bullet">
    ///   <item>split on dots, trimming each segment and dropping the empty ones — the
    ///   <c>[\s.]*</c> runs absorb both;</item>
    ///   <item>the first two surviving segments must parse as integers, sign allowed
    ///   (<c>[+-]?[0-9]{1,9}</c>), and the match must end at a dot or at the end of the
    ///   string, so a segment like <c>4x</c> fails the parse exactly as
    ///   <c>int.TryParse</c> does;</item>
    ///   <item>each captured segment is re-rendered through <c>::int::text</c>, so
    ///   <c>16.04.5</c> normalises to <c>16.4</c> the way <c>ToMajorMinor()</c> does;</item>
    ///   <item>no match ⇒ <c>regexp_match</c> yields NULL ⇒ the concatenation is NULL,
    ///   which is this column's "not a patch" value — the same answer callers get when
    ///   <c>TryParse</c> returns false.</item>
    /// </list>
    /// The one deliberate divergence is the <c>{1,9}</c> digit cap: a ten-digit segment
    /// that <c>int.TryParse</c> would still accept yields NULL here rather than an
    /// out-of-range cast that would fail the INSERT. No Riot version comes close.
    /// </summary>
    public const string PatchComputedColumnSql =
        """((regexp_match("GameVersion", '^[\s.]*([+-]?[0-9]{1,9})[\s]*\.[\s.]*([+-]?[0-9]{1,9})[\s]*(\.|$)'))[1])::int::text || '.' || ((regexp_match("GameVersion", '^[\s.]*([+-]?[0-9]{1,9})[\s]*\.[\s.]*([+-]?[0-9]{1,9})[\s]*(\.|$)'))[2])::int::text""";

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

        // Stored, not virtual: the point of the column is to be indexed, and Postgres
        // cannot index a virtual generated column. The database writes it on every
        // insert and on every update of GameVersion; the application never does, hence
        // the private setter on the entity.
        entity.Property(e => e.Patch)
            .HasMaxLength(32)
            .HasComputedColumnSql(PatchComputedColumnSql, stored: true);

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

        // Every champion read narrows the same way: this queue, this patch, then a join
        // back to match_participants on the primary key. Patch leads because it is the
        // selective half (one patch out of the few retained); QueueId follows so the
        // ranked-only filter is satisfied from the index too (#1368).
        entity.HasIndex(e => new { e.Patch, e.QueueId })
            .HasDatabaseName("IX_matches_patch_queue");

        // Queue-first sibling for the two readers that enumerate patches instead of
        // filtering on one: the pattern aggregation's DISTINCT (Patch, PlatformId) live
        // keys and retention's per-platform live window. Both become index-only scans
        // instead of a full pass over matches.
        entity.HasIndex(e => new { e.QueueId, e.Patch, e.PlatformId })
            .HasDatabaseName("IX_matches_queue_patch_platform");

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

        // Deliberately Restrict, and the only child of matches that is — match_bans,
        // match_participant_kill_positions, match_participant_timeline_snapshots and
        // participant_perk_selections all Cascade, so retention needs no extra arm for
        // them. Participants are the widest child table by far (ten rows per match, each
        // carrying the ItemEvents/SkillEvents jsonb), and a whole patch dropping out of
        // the window already blew the command timeout once when the delete cascaded
        // unbounded (#988). Restrict forces the caller to delete them itself, in bounded
        // committed batches, instead of hiding millions of rows behind one DELETE FROM
        // matches: see MatchDataRetentionProcess.DeleteExpiredMatchDataAsync and
        // DeleteNonRankedMatchDataAsync, which delete participants first for this reason.
        // Consequence to know before changing this: a bare `DELETE FROM matches` fails
        // with a foreign-key violation until participants are gone.
        entity.HasMany(e => e.Participants)
            .WithOne(e => e.Match)
            .HasForeignKey(e => e.MatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
