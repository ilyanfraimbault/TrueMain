using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonaAndRiotAccountFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastProfileSyncAtUtc",
                table: "players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PersonaId",
                table: "players",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProfileIconId",
                table: "players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SummonerId",
                table: "players",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SummonerLevel",
                table: "players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "players",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.CreateTable(
                name: "personas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_players_PersonaId",
                table: "players",
                column: "PersonaId");

            migrationBuilder.AddForeignKey(
                name: "FK_players_personas_PersonaId",
                table: "players",
                column: "PersonaId",
                principalTable: "personas",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_players_personas_PersonaId",
                table: "players");

            migrationBuilder.DropTable(
                name: "personas");

            migrationBuilder.DropIndex(
                name: "IX_players_PersonaId",
                table: "players");

            migrationBuilder.DropColumn(
                name: "LastProfileSyncAtUtc",
                table: "players");

            migrationBuilder.DropColumn(
                name: "PersonaId",
                table: "players");

            migrationBuilder.DropColumn(
                name: "ProfileIconId",
                table: "players");

            migrationBuilder.DropColumn(
                name: "SummonerId",
                table: "players");

            migrationBuilder.DropColumn(
                name: "SummonerLevel",
                table: "players");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "players");
        }
    }
}
