using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionNextItemTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "champion_next_item_terms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Slot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ParentItemId = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Axis = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Bucket = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Weight = table.Column<double>(type: "double precision", precision: 18, scale: 6, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    BranchGames = table.Column<int>(type: "integer", nullable: false),
                    PatchWindow = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_next_item_terms", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_champion_next_item_terms_grain",
                table: "champion_next_item_terms",
                columns: new[] { "Patch", "ChampionId", "Position", "Slot", "ParentItemId", "ItemId", "Axis", "Bucket" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_next_item_terms");
        }
    }
}
