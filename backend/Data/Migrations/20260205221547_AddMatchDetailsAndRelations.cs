using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchDetailsAndRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "matches",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "GameMode",
                table: "matches",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GameType",
                table: "matches",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MapId",
                table: "matches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_match_participants_MatchId",
                table: "match_participants",
                column: "MatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_match_participants_matches_MatchId",
                table: "match_participants",
                column: "MatchId",
                principalTable: "matches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_match_participants_matches_MatchId",
                table: "match_participants");

            migrationBuilder.DropIndex(
                name: "IX_match_participants_MatchId",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "GameMode",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "GameType",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "MapId",
                table: "matches");
        }
    }
}
