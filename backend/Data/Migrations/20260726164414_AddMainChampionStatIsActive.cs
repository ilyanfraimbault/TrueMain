using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMainChampionStatIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_main_champion_stats_is_main_champion",
                table: "main_champion_stats");

            migrationBuilder.DropIndex(
                name: "IX_main_champion_stats_PlatformId_IsMain",
                table: "main_champion_stats");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityCheckAtUtc",
                table: "riot_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "main_champion_stats",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_main_champion_stats_is_main_champion",
                table: "main_champion_stats",
                column: "ChampionId",
                filter: "\"IsMain\" AND \"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_main_champion_stats_PlatformId_IsMain_IsActive",
                table: "main_champion_stats",
                columns: new[] { "PlatformId", "IsMain", "IsActive" })
                .Annotation("Npgsql:IndexInclude", new[] { "Puuid" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_main_champion_stats_is_main_champion",
                table: "main_champion_stats");

            migrationBuilder.DropIndex(
                name: "IX_main_champion_stats_PlatformId_IsMain_IsActive",
                table: "main_champion_stats");

            migrationBuilder.DropColumn(
                name: "LastActivityCheckAtUtc",
                table: "riot_accounts");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "main_champion_stats");

            migrationBuilder.CreateIndex(
                name: "IX_main_champion_stats_is_main_champion",
                table: "main_champion_stats",
                column: "ChampionId",
                filter: "\"IsMain\"");

            migrationBuilder.CreateIndex(
                name: "IX_main_champion_stats_PlatformId_IsMain",
                table: "main_champion_stats",
                columns: new[] { "PlatformId", "IsMain" })
                .Annotation("Npgsql:IndexInclude", new[] { "Puuid" });
        }
    }
}
