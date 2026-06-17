using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchParticipantTimelineSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "match_participant_timeline_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    IntervalMinute = table.Column<int>(type: "integer", nullable: false),
                    TimestampMs = table.Column<int>(type: "integer", nullable: false),
                    TotalGold = table.Column<int>(type: "integer", nullable: false),
                    MinionsKilled = table.Column<int>(type: "integer", nullable: false),
                    JungleMinionsKilled = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Xp = table.Column<int>(type: "integer", nullable: false),
                    Kills = table.Column<int>(type: "integer", nullable: false),
                    DamageToChampions = table.Column<int>(type: "integer", nullable: false),
                    WardsPlaced = table.Column<int>(type: "integer", nullable: false),
                    WardsKilled = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_participant_timeline_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_match_participant_timeline_snapshots_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_match_participant_timeline_snapshots_MatchId_ParticipantId_~",
                table: "match_participant_timeline_snapshots",
                columns: new[] { "MatchId", "ParticipantId", "IntervalMinute" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_participant_timeline_snapshots");
        }
    }
}
