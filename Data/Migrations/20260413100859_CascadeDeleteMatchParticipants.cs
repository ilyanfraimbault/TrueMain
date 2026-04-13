using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class CascadeDeleteMatchParticipants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_match_participants_matches_MatchId",
                table: "match_participants");

            migrationBuilder.AddForeignKey(
                name: "FK_match_participants_matches_MatchId",
                table: "match_participants",
                column: "MatchId",
                principalTable: "matches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_match_participants_matches_MatchId",
                table: "match_participants");

            migrationBuilder.AddForeignKey(
                name: "FK_match_participants_matches_MatchId",
                table: "match_participants",
                column: "MatchId",
                principalTable: "matches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
