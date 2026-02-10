using System;
using System.Collections.Generic;
using Data.Entities;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMainChampionStatsAndLastMainCalcAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastMainCalcAtUtc",
                table: "riot_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "main_champion_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Puuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    TotalMatches = table.Column<int>(type: "integer", nullable: false),
                    ChampionMatches = table.Column<int>(type: "integer", nullable: false),
                    PlayRate = table.Column<double>(type: "double precision", nullable: false),
                    IsMain = table.Column<bool>(type: "boolean", nullable: false),
                    PrimaryPosition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PositionBreakdown = table.Column<List<PositionStat>>(type: "jsonb", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_main_champion_stats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_main_champion_stats_IsMain_PlayRate",
                table: "main_champion_stats",
                columns: new[] { "IsMain", "PlayRate" });

            migrationBuilder.CreateIndex(
                name: "IX_main_champion_stats_PlatformId_Puuid",
                table: "main_champion_stats",
                columns: new[] { "PlatformId", "Puuid" });

            migrationBuilder.CreateIndex(
                name: "IX_main_champion_stats_PlatformId_Puuid_ChampionId",
                table: "main_champion_stats",
                columns: new[] { "PlatformId", "Puuid", "ChampionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "main_champion_stats");

            migrationBuilder.DropColumn(
                name: "LastMainCalcAtUtc",
                table: "riot_accounts");
        }
    }
}
