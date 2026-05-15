using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class RenamePlayersToRiotAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_players_personas_PersonaId",
                table: "players");

            migrationBuilder.DropPrimaryKey(
                name: "PK_players",
                table: "players");

            migrationBuilder.RenameTable(
                name: "players",
                newName: "riot_accounts");

            migrationBuilder.RenameColumn(
                name: "Region",
                table: "riot_accounts",
                newName: "PlatformId");

            migrationBuilder.RenameIndex(
                name: "IX_players_Puuid",
                table: "riot_accounts",
                newName: "IX_riot_accounts_Puuid");

            migrationBuilder.RenameIndex(
                name: "IX_players_PersonaId",
                table: "riot_accounts",
                newName: "IX_riot_accounts_PersonaId");

            migrationBuilder.RenameIndex(
                name: "IX_players_GameName_TagLine_Region",
                table: "riot_accounts",
                newName: "IX_riot_accounts_GameName_TagLine_PlatformId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_riot_accounts",
                table: "riot_accounts",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_riot_accounts_personas_PersonaId",
                table: "riot_accounts",
                column: "PersonaId",
                principalTable: "personas",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_riot_accounts_personas_PersonaId",
                table: "riot_accounts");

            migrationBuilder.DropPrimaryKey(
                name: "PK_riot_accounts",
                table: "riot_accounts");

            migrationBuilder.RenameTable(
                name: "riot_accounts",
                newName: "players");

            migrationBuilder.RenameColumn(
                name: "PlatformId",
                table: "players",
                newName: "Region");

            migrationBuilder.RenameIndex(
                name: "IX_riot_accounts_Puuid",
                table: "players",
                newName: "IX_players_Puuid");

            migrationBuilder.RenameIndex(
                name: "IX_riot_accounts_PersonaId",
                table: "players",
                newName: "IX_players_PersonaId");

            migrationBuilder.RenameIndex(
                name: "IX_riot_accounts_GameName_TagLine_PlatformId",
                table: "players",
                newName: "IX_players_GameName_TagLine_Region");

            migrationBuilder.AddPrimaryKey(
                name: "PK_players",
                table: "players",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_players_personas_PersonaId",
                table: "players",
                column: "PersonaId",
                principalTable: "personas",
                principalColumn: "Id");
        }
    }
}
