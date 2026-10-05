using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionDamageProfileStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "champion_damage_profile_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Archetype = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    PhysicalDamageToChampionsSum = table.Column<long>(type: "bigint", nullable: false),
                    MagicDamageToChampionsSum = table.Column<long>(type: "bigint", nullable: false),
                    TrueDamageToChampionsSum = table.Column<long>(type: "bigint", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_damage_profile_stats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_champion_damage_profile_stats_grain",
                table: "champion_damage_profile_stats",
                columns: new[] { "Patch", "ChampionId", "Position", "Archetype" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_damage_profile_stats");
        }
    }
}
