using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionPatchStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "champion_patch_stats",
                columns: table => new
                {
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EloBracket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsMain = table.Column<bool>(type: "boolean", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_patch_stats", x => new { x.QueueId, x.Patch, x.Position, x.EloBracket, x.IsMain, x.ChampionId });
                    table.CheckConstraint("CK_champion_patch_stats_WinsWithinGames", "\"Wins\" <= \"Games\"");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_patch_stats");
        }
    }
}
