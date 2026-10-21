using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionScalingStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ScalingAggregated",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "champion_scaling_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EloBracket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Bucket = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_scaling_stats", x => x.Id);
                    table.CheckConstraint("CK_champion_scaling_stats_WinsWithinGames", "\"Wins\" <= \"Games\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_scaling_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"ScalingAggregated\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_champion_scaling_stats_grain",
                table: "champion_scaling_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "EloBracket", "Bucket" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_scaling_stats");

            migrationBuilder.DropIndex(
                name: "IX_matches_scaling_pending",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "ScalingAggregated",
                table: "matches");
        }
    }
}
