using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLadderGamesToRiotAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LadderGames",
                table: "riot_accounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LadderGamesAtLastIngest",
                table: "riot_accounts",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LadderGames",
                table: "riot_accounts");

            migrationBuilder.DropColumn(
                name: "LadderGamesAtLastIngest",
                table: "riot_accounts");
        }
    }
}
