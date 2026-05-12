using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchParticipantRiotAccountId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RiotAccountId",
                table: "match_participants",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "match_participants" p
                SET "RiotAccountId" = r."Id"
                FROM "riot_accounts" r,
                     "matches" m
                WHERE p."RiotAccountId" IS NULL
                  AND m."Id" = p."MatchId"
                  AND p."Puuid" = r."Puuid"
                  AND m."PlatformId" = r."PlatformId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_match_participants_RiotAccountId",
                table: "match_participants",
                column: "RiotAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_match_participants_riot_accounts_RiotAccountId",
                table: "match_participants",
                column: "RiotAccountId",
                principalTable: "riot_accounts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_match_participants_riot_accounts_RiotAccountId",
                table: "match_participants");

            migrationBuilder.DropIndex(
                name: "IX_match_participants_RiotAccountId",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "RiotAccountId",
                table: "match_participants");
        }
    }
}
