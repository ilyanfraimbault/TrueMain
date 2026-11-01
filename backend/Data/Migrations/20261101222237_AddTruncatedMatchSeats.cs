using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTruncatedMatchSeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "truncated_match_seats",
                columns: table => new
                {
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    PlatformId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GameVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GameStartTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GameDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    EloBracket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    Win = table.Column<bool>(type: "boolean", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RiotAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Puuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Summoner1Id = table.Column<int>(type: "integer", nullable: false),
                    Summoner2Id = table.Column<int>(type: "integer", nullable: false),
                    PrimaryStyleId = table.Column<int>(type: "integer", nullable: false),
                    SubStyleId = table.Column<int>(type: "integer", nullable: false),
                    RunePerkIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    PerksOffense = table.Column<int>(type: "integer", nullable: false),
                    PerksFlex = table.Column<int>(type: "integer", nullable: false),
                    PerksDefense = table.Column<int>(type: "integer", nullable: false),
                    StarterItemIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    BuildItemIds = table.Column<int[]>(type: "integer[]", nullable: false),
                    BuildItemMinutes = table.Column<int[]>(type: "integer[]", nullable: false),
                    BootsItemId = table.Column<int>(type: "integer", nullable: false),
                    SkillOrderKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kills = table.Column<int>(type: "integer", nullable: false),
                    Deaths = table.Column<int>(type: "integer", nullable: false),
                    Assists = table.Column<int>(type: "integer", nullable: false),
                    TruncatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_truncated_match_seats", x => new { x.MatchId, x.ParticipantId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_truncated_match_seats_account",
                table: "truncated_match_seats",
                column: "RiotAccountId",
                filter: "\"RiotAccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_truncated_match_seats_champion_position_patch",
                table: "truncated_match_seats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch" });

            migrationBuilder.CreateIndex(
                name: "IX_truncated_match_seats_patch",
                table: "truncated_match_seats",
                column: "Patch");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "truncated_match_seats");
        }
    }
}
