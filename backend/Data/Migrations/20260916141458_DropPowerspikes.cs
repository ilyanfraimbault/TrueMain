using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Drops the power spikes feature: its three aggregate tables, the per-match fold flag and
    /// that flag's pending index. The snapshot-prune index is re-keyed from the fold flag to
    /// <c>TimelineIngested</c>, since retention now prunes every timeline-ingested match's legacy
    /// per-minute grid instead of waiting for a fold that no longer exists. Dropping tables and a
    /// column is catalog-only in Postgres; the rebuilt partial index covers only the unpruned tail.
    /// </summary>
    public partial class DropPowerspikes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_powerspike_curve_stats");

            migrationBuilder.DropTable(
                name: "champion_powerspike_event_stats");

            migrationBuilder.DropTable(
                name: "powerspike_sigma_stats");

            migrationBuilder.DropIndex(
                name: "IX_matches_powerspike_pending",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "IX_matches_snapshot_prune_pending",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "PowerspikeAggregated",
                table: "matches");

            migrationBuilder.CreateIndex(
                name: "IX_matches_snapshot_prune_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"TimelineIngested\" = true AND \"TimelineSnapshotsPruned\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_matches_snapshot_prune_pending",
                table: "matches");

            migrationBuilder.AddColumn<bool>(
                name: "PowerspikeAggregated",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "champion_powerspike_curve_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    elo_bracket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    IntervalMinute = table.Column<int>(type: "integer", nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TotalDamageDiff = table.Column<long>(type: "bigint", nullable: false),
                    TotalGoldDiff = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_powerspike_curve_stats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "champion_powerspike_event_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BuildFirstItemId = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    BuildKeystoneId = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    elo_bracket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    EventType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    OpponentChampionId = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RefId = table.Column<int>(type: "integer", nullable: false),
                    SumMinute = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    SumSpike = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_powerspike_event_stats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "powerspike_sigma_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IntervalMinute = table.Column<int>(type: "integer", nullable: false),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    SampleCount = table.Column<long>(type: "bigint", nullable: false),
                    SumDamageDiff = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    SumDamageDiffSq = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    SumGoldDiff = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    SumGoldDiffSq = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_powerspike_sigma_stats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_powerspike_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"PowerspikeAggregated\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_matches_snapshot_prune_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"PowerspikeAggregated\" = true AND \"TimelineSnapshotsPruned\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_champion_powerspike_curve_stats_ChampionId_TeamPosition_Pat~",
                table: "champion_powerspike_curve_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "elo_bracket", "IntervalMinute" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_powerspike_event_stats_ChampionId_TeamPosition_Pat~",
                table: "champion_powerspike_event_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "elo_bracket", "BuildFirstItemId", "BuildKeystoneId", "OpponentChampionId", "EventType", "RefId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_powerspike_sigma_stats_QueueId_IntervalMinute",
                table: "powerspike_sigma_stats",
                columns: new[] { "QueueId", "IntervalMinute" },
                unique: true);
        }
    }
}
