using System;
using System.Collections.Generic;
using Data.ItemContext;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionItemContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ItemContextAggregated",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "champion_item_context_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Slot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Axis = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Bucket = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_item_context_stats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "champion_item_context_totals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Slot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Axis = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Bucket = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_item_context_totals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "champion_item_context_verdicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Slot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    SlotGames = table.Column<int>(type: "integer", nullable: false),
                    PickRate = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    Class = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PatchWindow = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Axes = table.Column<List<ItemContextAxisFinding>>(type: "jsonb", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_item_context_verdicts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_item_context_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"ItemContextAggregated\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_champion_item_context_stats_grain",
                table: "champion_item_context_stats",
                columns: new[] { "Patch", "ChampionId", "Position", "Slot", "ItemId", "Axis", "Bucket" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_item_context_totals_grain",
                table: "champion_item_context_totals",
                columns: new[] { "Patch", "ChampionId", "Position", "Slot", "Axis", "Bucket" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_item_context_verdicts_grain",
                table: "champion_item_context_verdicts",
                columns: new[] { "Patch", "ChampionId", "Position", "Slot", "ItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_item_context_stats");

            migrationBuilder.DropTable(
                name: "champion_item_context_totals");

            migrationBuilder.DropTable(
                name: "champion_item_context_verdicts");

            migrationBuilder.DropIndex(
                name: "IX_matches_item_context_pending",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "ItemContextAggregated",
                table: "matches");
        }
    }
}
