using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class DropChampionTimelineLeadStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_timeline_lead_stats");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "champion_timeline_lead_stats",
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
                    TotalCsDiff = table.Column<long>(type: "bigint", nullable: false),
                    TotalDamageDiff = table.Column<long>(type: "bigint", nullable: false),
                    TotalGoldDiff = table.Column<long>(type: "bigint", nullable: false),
                    TotalKillsDiff = table.Column<long>(type: "bigint", nullable: false),
                    TotalLevelDiff = table.Column<long>(type: "bigint", nullable: false),
                    TotalXpDiff = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_timeline_lead_stats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_champion_timeline_lead_stats_ChampionId_TeamPosition_Patch_~",
                table: "champion_timeline_lead_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "IntervalMinute", "elo_bracket" },
                unique: true);
        }
    }
}
