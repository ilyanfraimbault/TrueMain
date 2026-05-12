using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMainCandidate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TagLine",
                table: "riot_accounts",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);

            migrationBuilder.CreateTable(
                name: "main_candidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Puuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    ChampionRankInMasteryTop = table.Column<int>(type: "integer", nullable: false),
                    ChampionPoints = table.Column<long>(type: "bigint", nullable: false),
                    LastPlayTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DiscoveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_main_candidates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_main_candidates_ChampionId",
                table: "main_candidates",
                column: "ChampionId");

            migrationBuilder.CreateIndex(
                name: "IX_main_candidates_PlatformId",
                table: "main_candidates",
                column: "PlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_main_candidates_PlatformId_Puuid_ChampionId",
                table: "main_candidates",
                columns: new[] { "PlatformId", "Puuid", "ChampionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "main_candidates");

            migrationBuilder.AlterColumn<string>(
                name: "TagLine",
                table: "riot_accounts",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8,
                oldNullable: true);
        }
    }
}
