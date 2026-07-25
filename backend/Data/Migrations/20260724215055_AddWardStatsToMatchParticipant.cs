using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWardStatsToMatchParticipant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DetectorWardsPlaced",
                table: "match_participants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WardsKilled",
                table: "match_participants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WardsPlaced",
                table: "match_participants",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectorWardsPlaced",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "WardsKilled",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "WardsPlaced",
                table: "match_participants");
        }
    }
}
