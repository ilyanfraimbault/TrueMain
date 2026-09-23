using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Data.Configurations;

public sealed class MatchParticipantConfiguration : IEntityTypeConfiguration<MatchParticipant>
{
    public void Configure(EntityTypeBuilder<MatchParticipant> entity)
    {
        entity.ToTable("match_participants");

        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        entity.Property(e => e.MatchId)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.ParticipantId)
            .IsRequired();

        entity.Property(e => e.Puuid)
            .IsRequired()
            .HasMaxLength(128);

        entity.Property(e => e.RiotAccountId);

        entity.Property(e => e.SummonerName)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.SummonerLevel)
            .IsRequired();

        entity.Property(e => e.ChampionId)
            .IsRequired();

        entity.Property(e => e.TeamId)
            .IsRequired();

        entity.Property(e => e.TeamPosition)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.EloBracket)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnName("elo_bracket")
            .HasDefaultValue(string.Empty);

        entity.Property(e => e.IndividualPosition)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.Lane)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.Role)
            .IsRequired()
            .HasMaxLength(32);

        entity.Property(e => e.Win)
            .IsRequired();

        entity.Property(e => e.Kills)
            .IsRequired();

        entity.Property(e => e.Deaths)
            .IsRequired();

        entity.Property(e => e.Assists)
            .IsRequired();

        entity.Property(e => e.TotalDamageDealtToChampions)
            .IsRequired()
            .HasDefaultValue(0);

        entity.Property(e => e.PhysicalDamageDealtToChampions)
            .IsRequired(false);

        entity.Property(e => e.MagicDamageDealtToChampions)
            .IsRequired(false);

        entity.Property(e => e.TrueDamageDealtToChampions)
            .IsRequired(false);

        entity.Property(e => e.TotalHeal)
            .IsRequired(false);

        entity.Property(e => e.TotalHealsOnTeammates)
            .IsRequired(false);

        entity.Property(e => e.TotalDamageShieldedOnTeammates)
            .IsRequired(false);

        entity.Property(e => e.TimeCCingOthers)
            .IsRequired(false);

        entity.Property(e => e.TotalTimeCCDealt)
            .IsRequired(false);

        entity.Property(e => e.TotalDamageTaken)
            .IsRequired(false);

        entity.Property(e => e.DamageSelfMitigated)
            .IsRequired(false);

        entity.Property(e => e.VisionScore)
            .IsRequired()
            .HasDefaultValue(0);

        entity.Property(e => e.GoldEarned)
            .IsRequired();

        entity.Property(e => e.TotalMinionsKilled)
            .IsRequired();

        entity.Property(e => e.NeutralMinionsKilled)
            .IsRequired();

        entity.Property(e => e.ChampLevel)
            .IsRequired();

        entity.Property(e => e.Item0)
            .IsRequired();
        entity.Property(e => e.Item1)
            .IsRequired();
        entity.Property(e => e.Item2)
            .IsRequired();
        entity.Property(e => e.Item3)
            .IsRequired();
        entity.Property(e => e.Item4)
            .IsRequired();
        entity.Property(e => e.Item5)
            .IsRequired();
        entity.Property(e => e.Item6)
            .IsRequired();

        entity.Property(e => e.TrinketItemId)
            .IsRequired();

        entity.Property(e => e.PerksDefense)
            .IsRequired();
        entity.Property(e => e.PerksFlex)
            .IsRequired();
        entity.Property(e => e.PerksOffense)
            .IsRequired();
        entity.Property(e => e.PrimaryStyleId)
            .IsRequired();
        entity.Property(e => e.SubStyleId)
            .IsRequired();

        entity.Property(e => e.Summoner1Id)
            .IsRequired();
        entity.Property(e => e.Summoner2Id)
            .IsRequired();

        entity.Property(e => e.ItemEvents)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(e => e.SkillEvents)
            .HasColumnType("jsonb")
            .IsRequired();

        entity.HasIndex(e => new { e.Puuid, e.MatchId })
            .HasDatabaseName("IX_match_participants_puuid_match");

        // The natural key, and — since #1663 — the index that answers "who played what in
        // this match" without touching the table. Every live read of a draft probes
        // participants by MatchId and needs only these few narrow columns; carrying them
        // here turns those probes into index-only scans.
        //
        // Measured locally on a 2M-row / 776 MB reconstruction of the table, cold page
        // cache, the composition recommender's pinned-matchup query: 24 556 buffers read
        // and ~700 ms before, 1 715 and ~100 ms after (both on a vacuumed table, so the
        // gain is the index, not the vacuum). The scans it replaced read all ten
        // participants of a candidate match from the heap to keep one.
        //
        // `Puuid` and `Win` are the costly pair to carry and the reason it works: without
        // them the probe still falls back to the heap for every row. The whole set adds
        // ~25% to the table's total size, bought back many times over on every draft edit.
        entity.HasIndex(e => new { e.MatchId, e.ParticipantId })
            .IsUnique()
            .IncludeProperties(e => new
            {
                e.TeamId,
                e.TeamPosition,
                e.ChampionId,
                e.Win,
                e.Puuid,
            });

        entity.HasIndex(e => e.RiotAccountId);

        // The champion-page reads (builds, matchups, scaling, leads, item-timings)
        // all filter the tracked-account rows by champion + lane. A partial
        // index on those columns (only the tracked rows, ~1/10 of the table) turns
        // those filters into an index seek instead of a scan of the full 35 GB
        // match_participants table. EloBracket is the trailing column so the same
        // index serves both the unfiltered (champion, lane) prefix reads and the
        // rank-filtered (champion, lane, band) reads.
        // Both indexes below share the same columns, so each needs an explicit
        // model name — two unnamed HasIndex calls on the same property set would
        // collapse into a single model index.
        entity.HasIndex(
                e => new { e.ChampionId, e.TeamPosition, e.EloBracket },
                "IX_match_participants_champion_position_tracked")
            .HasFilter("\"RiotAccountId\" IS NOT NULL");

        // The composition-build recommender (#563) searches the FULL participant
        // pool — harvested rows included, because itemization/rune data is valid
        // regardless of whether the player is tracked. The partial index above
        // cannot serve that read, so this one carries no filter. Same column
        // order: the (champion, lane) prefix serves the unfiltered search and
        // EloBracket keeps a future rank filter cheap. In prod this index must be
        // pre-created out-of-band (CREATE INDEX CONCURRENTLY) — the migration is
        // IF NOT EXISTS so startup stays a fast no-op.
        entity.HasIndex(
                e => new { e.ChampionId, e.TeamPosition, e.EloBracket },
                "IX_match_participants_champion_position_full")
            // Covering since #1663, for the same reason as the unique index above: the
            // selection reads a champion's rows by (champion, lane) and needs only these
            // columns from them. Measured on the same reconstruction, the *unpinned*
            // composition read — where the pool is the champion's own history rather than
            // one matchup's — went 307 ms -> 211 ms cold with this on top of the other.
            .IncludeProperties(e => new
            {
                e.MatchId,
                e.ParticipantId,
                e.TeamId,
                e.Win,
                e.Puuid,
            });

        // The role-bound backfill's work queue (#1612): bot-lane rows ingested before
        // the column existed. Partial, so it holds only what is left to drain and the
        // steady-state scan is an empty index read instead of a pass over the table.
        entity.HasIndex(e => e.Id, "IX_match_participants_role_bound_pending")
            .HasFilter("\"RoleBoundItemId\" IS NULL AND \"TeamPosition\" = 'BOTTOM'");

        entity.HasOne(e => e.RiotAccount)
            .WithMany()
            .HasForeignKey(e => e.RiotAccountId);
    }
}
