using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class DropTimelineSnapshotsAddPowerspikeAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TimelineAggregated",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "champion_powerspike_event_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EventType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    RefId = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    SumEventMinute = table.Column<long>(type: "bigint", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_powerspike_event_stats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "timeline_lead_sigma_moments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IntervalMinute = table.Column<int>(type: "integer", nullable: false),
                    N = table.Column<long>(type: "bigint", nullable: false),
                    SumGold = table.Column<double>(type: "double precision", nullable: false),
                    SumSqGold = table.Column<double>(type: "double precision", nullable: false),
                    SumDmg = table.Column<double>(type: "double precision", nullable: false),
                    SumSqDmg = table.Column<double>(type: "double precision", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_timeline_lead_sigma_moments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_champion_powerspike_event_stats_ChampionId_TeamPosition_Pat~",
                table: "champion_powerspike_event_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "EventType", "RefId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_timeline_lead_sigma_moments_QueueId_Patch_IntervalMinute",
                table: "timeline_lead_sigma_moments",
                columns: new[] { "QueueId", "Patch", "IntervalMinute" },
                unique: true);

            // One-time backfill: compress the raw per-minute snapshot grid into the
            // new aggregates before dropping it, so no history is lost. Each statement
            // is a server-side GROUP BY (only aggregated rows are produced), ON CONFLICT
            // DO NOTHING so it is idempotent and never disturbs rows the batch already
            // wrote or patches that have aged out of `matches` (their snapshots are
            // gone, so they are simply absent here and stay frozen).
            //
            // Patch normalisation mirrors Core PatchVersion.Normalize (major.minor).
            // Lead + event backfill pin queue 420 (RankedSoloDuo, the only queue the
            // champion reads slice) to match the incremental writer; sigma is keyed by
            // queue, so it seeds every queue harmlessly and the read picks 420.
            BackfillLeads(migrationBuilder);
            BackfillSigmaMoments(migrationBuilder);
            BackfillLevelEvents(migrationBuilder);
            BackfillItemEvents(migrationBuilder);

            // Every already-ingested match is now represented in the aggregates, so
            // claim it: a future re-ingestion must not double-count into the add-only
            // accumulators. New matches keep the false default and accumulate once.
            migrationBuilder.Sql(@"UPDATE matches SET ""TimelineAggregated"" = true WHERE ""TimelineIngested"" = true;");

            migrationBuilder.DropTable(
                name: "match_participant_timeline_snapshots");
        }

        private const string PatchExpr =
            @"split_part(m.""GameVersion"", '.', 1) || '.' || split_part(m.""GameVersion"", '.', 2)";

        private const string CanonicalPositions = "'TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'";

        private static void BackfillLeads(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql($@"
                INSERT INTO champion_timeline_lead_stats
                    (""Id"", ""ChampionId"", ""TeamPosition"", ""Patch"", ""IntervalMinute"", ""Games"",
                     ""TotalGoldDiff"", ""TotalCsDiff"", ""TotalKillsDiff"", ""TotalLevelDiff"", ""TotalXpDiff"", ""TotalDamageDiff"", ""AggregatedAtUtc"")
                SELECT gen_random_uuid(),
                       p1.""ChampionId"",
                       p1.""TeamPosition"",
                       {PatchExpr},
                       s1.""IntervalMinute"",
                       COUNT(*),
                       SUM((s1.""TotalGold"" - s2.""TotalGold"")::bigint),
                       SUM(((s1.""MinionsKilled"" + s1.""JungleMinionsKilled"") - (s2.""MinionsKilled"" + s2.""JungleMinionsKilled""))::bigint),
                       SUM((s1.""Kills"" - s2.""Kills"")::bigint),
                       SUM((s1.""Level"" - s2.""Level"")::bigint),
                       SUM((s1.""Xp"" - s2.""Xp"")::bigint),
                       SUM((s1.""DamageToChampions"" - s2.""DamageToChampions"")::bigint),
                       now()
                FROM match_participants p1
                JOIN matches m ON m.""Id"" = p1.""MatchId"" AND m.""QueueId"" = 420
                JOIN match_participants p2 ON p2.""MatchId"" = p1.""MatchId""
                    AND p2.""TeamPosition"" = p1.""TeamPosition"" AND p2.""TeamId"" <> p1.""TeamId""
                JOIN match_participant_timeline_snapshots s1 ON s1.""MatchId"" = p1.""MatchId"" AND s1.""ParticipantId"" = p1.""ParticipantId""
                JOIN match_participant_timeline_snapshots s2 ON s2.""MatchId"" = p2.""MatchId""
                    AND s2.""ParticipantId"" = p2.""ParticipantId"" AND s2.""IntervalMinute"" = s1.""IntervalMinute""
                WHERE p1.""RiotAccountId"" IS NOT NULL
                  AND p1.""TeamPosition"" IN ({CanonicalPositions})
                GROUP BY p1.""ChampionId"", p1.""TeamPosition"", {PatchExpr}, s1.""IntervalMinute""
                ON CONFLICT (""ChampionId"", ""TeamPosition"", ""Patch"", ""IntervalMinute"") DO NOTHING;");

        private static void BackfillSigmaMoments(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql($@"
                INSERT INTO timeline_lead_sigma_moments
                    (""Id"", ""QueueId"", ""Patch"", ""IntervalMinute"", ""N"", ""SumGold"", ""SumSqGold"", ""SumDmg"", ""SumSqDmg"", ""AggregatedAtUtc"")
                SELECT gen_random_uuid(),
                       m.""QueueId"",
                       {PatchExpr},
                       s1.""IntervalMinute"",
                       COUNT(*),
                       SUM((s1.""TotalGold"" - s2.""TotalGold"")::double precision),
                       SUM((s1.""TotalGold"" - s2.""TotalGold"")::double precision * (s1.""TotalGold"" - s2.""TotalGold"")::double precision),
                       SUM((s1.""DamageToChampions"" - s2.""DamageToChampions"")::double precision),
                       SUM((s1.""DamageToChampions"" - s2.""DamageToChampions"")::double precision * (s1.""DamageToChampions"" - s2.""DamageToChampions"")::double precision),
                       now()
                FROM match_participant_timeline_snapshots s1
                JOIN match_participants mp1 ON mp1.""MatchId"" = s1.""MatchId"" AND mp1.""ParticipantId"" = s1.""ParticipantId""
                JOIN match_participants mp2 ON mp2.""MatchId"" = s1.""MatchId""
                    AND mp2.""TeamPosition"" = mp1.""TeamPosition"" AND mp2.""TeamId"" <> mp1.""TeamId""
                JOIN match_participant_timeline_snapshots s2 ON s2.""MatchId"" = mp2.""MatchId""
                    AND s2.""ParticipantId"" = mp2.""ParticipantId"" AND s2.""IntervalMinute"" = s1.""IntervalMinute""
                JOIN matches m ON m.""Id"" = s1.""MatchId""
                WHERE mp1.""TeamPosition"" IN ({CanonicalPositions})
                GROUP BY m.""QueueId"", {PatchExpr}, s1.""IntervalMinute""
                ON CONFLICT (""QueueId"", ""Patch"", ""IntervalMinute"") DO NOTHING;");

        private static void BackfillLevelEvents(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql($@"
                INSERT INTO champion_powerspike_event_stats
                    (""Id"", ""ChampionId"", ""TeamPosition"", ""Patch"", ""EventType"", ""RefId"", ""Games"", ""SumEventMinute"", ""AggregatedAtUtc"")
                SELECT gen_random_uuid(), r.""ChampionId"", r.""TeamPosition"", r.patch, 'level', r.milestone, COUNT(*), SUM(r.reached), now()
                FROM (
                    SELECT p1.""ChampionId"",
                           p1.""TeamPosition"",
                           {PatchExpr} AS patch,
                           ml.milestone,
                           MIN(s1.""IntervalMinute"") AS reached
                    FROM match_participants p1
                    JOIN matches m ON m.""Id"" = p1.""MatchId"" AND m.""QueueId"" = 420
                    JOIN match_participants p2 ON p2.""MatchId"" = p1.""MatchId""
                        AND p2.""TeamPosition"" = p1.""TeamPosition"" AND p2.""TeamId"" <> p1.""TeamId""
                    JOIN match_participant_timeline_snapshots s1 ON s1.""MatchId"" = p1.""MatchId"" AND s1.""ParticipantId"" = p1.""ParticipantId""
                    CROSS JOIN (VALUES (6), (11), (16)) AS ml(milestone)
                    WHERE p1.""RiotAccountId"" IS NOT NULL
                      AND p1.""TeamPosition"" IN ({CanonicalPositions})
                      AND s1.""Level"" >= ml.milestone
                    GROUP BY p1.""ChampionId"", p1.""TeamPosition"", {PatchExpr}, ml.milestone, p1.""MatchId"", p1.""ParticipantId""
                ) r
                GROUP BY r.""ChampionId"", r.""TeamPosition"", r.patch, r.milestone
                ON CONFLICT (""ChampionId"", ""TeamPosition"", ""Patch"", ""EventType"", ""RefId"") DO NOTHING;");

        private static void BackfillItemEvents(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql($@"
                INSERT INTO champion_powerspike_event_stats
                    (""Id"", ""ChampionId"", ""TeamPosition"", ""Patch"", ""EventType"", ""RefId"", ""Games"", ""SumEventMinute"", ""AggregatedAtUtc"")
                SELECT gen_random_uuid(), r.""ChampionId"", r.""TeamPosition"", r.patch, 'item', r.item_id, COUNT(*), SUM(r.minute), now()
                FROM (
                    SELECT p1.""ChampionId"",
                           p1.""TeamPosition"",
                           {PatchExpr} AS patch,
                           (ev->>'ItemId')::int AS item_id,
                           ROUND(MIN((ev->>'TimestampMs')::numeric) / 60000.0)::int AS minute
                    FROM match_participants p1
                    JOIN matches m ON m.""Id"" = p1.""MatchId"" AND m.""QueueId"" = 420
                    JOIN match_participants p2 ON p2.""MatchId"" = p1.""MatchId""
                        AND p2.""TeamPosition"" = p1.""TeamPosition"" AND p2.""TeamId"" <> p1.""TeamId""
                    CROSS JOIN LATERAL jsonb_array_elements(p1.""ItemEvents"") ev
                    WHERE p1.""RiotAccountId"" IS NOT NULL
                      AND p1.""TeamPosition"" IN ({CanonicalPositions})
                      AND ev->>'EventType' = 'ITEM_PURCHASED'
                    GROUP BY p1.""ChampionId"", p1.""TeamPosition"", {PatchExpr}, p1.""MatchId"", p1.""ParticipantId"", (ev->>'ItemId')::int
                ) r
                GROUP BY r.""ChampionId"", r.""TeamPosition"", r.patch, r.item_id
                ON CONFLICT (""ChampionId"", ""TeamPosition"", ""Patch"", ""EventType"", ""RefId"") DO NOTHING;");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_powerspike_event_stats");

            migrationBuilder.DropTable(
                name: "timeline_lead_sigma_moments");

            migrationBuilder.DropColumn(
                name: "TimelineAggregated",
                table: "matches");

            migrationBuilder.CreateTable(
                name: "match_participant_timeline_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DamageToChampions = table.Column<int>(type: "integer", nullable: false),
                    IntervalMinute = table.Column<int>(type: "integer", nullable: false),
                    JungleMinionsKilled = table.Column<int>(type: "integer", nullable: false),
                    Kills = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MinionsKilled = table.Column<int>(type: "integer", nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    TimestampMs = table.Column<int>(type: "integer", nullable: false),
                    TotalGold = table.Column<int>(type: "integer", nullable: false),
                    WardsKilled = table.Column<int>(type: "integer", nullable: false),
                    WardsPlaced = table.Column<int>(type: "integer", nullable: false),
                    Xp = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_participant_timeline_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_match_participant_timeline_snapshots_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_match_participant_timeline_snapshots_MatchId_ParticipantId_~",
                table: "match_participant_timeline_snapshots",
                columns: new[] { "MatchId", "ParticipantId", "IntervalMinute" },
                unique: true);
        }
    }
}
