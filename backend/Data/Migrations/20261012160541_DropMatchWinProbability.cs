using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class DropMatchWinProbability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_win_probability");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "match_win_probability",
                columns: table => new
                {
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Objectives = table.Column<string>(type: "jsonb", nullable: false),
                    Points = table.Column<string>(type: "jsonb", nullable: false),
                    Swings = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_win_probability", x => x.MatchId);
                    table.ForeignKey(
                        name: "FK_match_win_probability_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }
    }
}
