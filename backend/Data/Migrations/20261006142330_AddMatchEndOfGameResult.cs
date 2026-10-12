using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchEndOfGameResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EndOfGameResult",
                table: "matches",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EndedInEarlySurrender",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "GameCreationUtc",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GameEndTimestampUtc",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndOfGameResult",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "EndedInEarlySurrender",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "GameCreationUtc",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "GameEndTimestampUtc",
                table: "matches");
        }
    }
}
