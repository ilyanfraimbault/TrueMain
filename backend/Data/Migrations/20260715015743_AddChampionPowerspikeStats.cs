using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionPowerspikeStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    elo_bracket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    IntervalMinute = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    TotalGoldDiff = table.Column<long>(type: "bigint", nullable: false),
                    TotalDamageDiff = table.Column<long>(type: "bigint", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    elo_bracket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    EventType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    RefId = table.Column<int>(type: "integer", nullable: false),
                    SumSpike = table.Column<double>(type: "double precision", nullable: false),
                    SumMinute = table.Column<double>(type: "double precision", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    IntervalMinute = table.Column<int>(type: "integer", nullable: false),
                    SumGoldDiff = table.Column<double>(type: "double precision", nullable: false),
                    SumGoldDiffSq = table.Column<double>(type: "double precision", nullable: false),
                    SumDamageDiff = table.Column<double>(type: "double precision", nullable: false),
                    SumDamageDiffSq = table.Column<double>(type: "double precision", nullable: false),
                    SampleCount = table.Column<long>(type: "bigint", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                name: "IX_champion_powerspike_curve_stats_ChampionId_TeamPosition_Pat~",
                table: "champion_powerspike_curve_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "elo_bracket", "IntervalMinute" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_powerspike_event_stats_ChampionId_TeamPosition_Pat~",
                table: "champion_powerspike_event_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "elo_bracket", "EventType", "RefId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_powerspike_sigma_stats_QueueId_IntervalMinute",
                table: "powerspike_sigma_stats",
                columns: new[] { "QueueId", "IntervalMinute" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropColumn(
                name: "PowerspikeAggregated",
                table: "matches");
        }
    }
}
