using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionGameCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "champion_game_counts",
                columns: table => new
                {
                    PlatformId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<long>(type: "bigint", nullable: false),
                    RefreshedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_game_counts", x => new { x.QueueId, x.Patch, x.PlatformId, x.TeamPosition, x.ChampionId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_game_counts");
        }
    }
}
