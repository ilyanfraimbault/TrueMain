using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchParticipantContextFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DamageSelfMitigated",
                table: "match_participants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MagicDamageDealtToChampions",
                table: "match_participants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhysicalDamageDealtToChampions",
                table: "match_participants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TimeCCingOthers",
                table: "match_participants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalDamageShieldedOnTeammates",
                table: "match_participants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalDamageTaken",
                table: "match_participants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalHeal",
                table: "match_participants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalHealsOnTeammates",
                table: "match_participants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalTimeCCDealt",
                table: "match_participants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrueDamageDealtToChampions",
                table: "match_participants",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DamageSelfMitigated",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "MagicDamageDealtToChampions",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "PhysicalDamageDealtToChampions",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "TimeCCingOthers",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "TotalDamageShieldedOnTeammates",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "TotalDamageTaken",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "TotalHeal",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "TotalHealsOnTeammates",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "TotalTimeCCDealt",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "TrueDamageDealtToChampions",
                table: "match_participants");
        }
    }
}
