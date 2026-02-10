using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class MatchParticipant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "match_participants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    Puuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SummonerName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SummonerLevel = table.Column<int>(type: "integer", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IndividualPosition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Lane = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Win = table.Column<bool>(type: "boolean", nullable: false),
                    Kills = table.Column<int>(type: "integer", nullable: false),
                    Deaths = table.Column<int>(type: "integer", nullable: false),
                    Assists = table.Column<int>(type: "integer", nullable: false),
                    GoldEarned = table.Column<int>(type: "integer", nullable: false),
                    TotalMinionsKilled = table.Column<int>(type: "integer", nullable: false),
                    NeutralMinionsKilled = table.Column<int>(type: "integer", nullable: false),
                    ChampLevel = table.Column<int>(type: "integer", nullable: false),
                    Item0 = table.Column<int>(type: "integer", nullable: false),
                    Item1 = table.Column<int>(type: "integer", nullable: false),
                    Item2 = table.Column<int>(type: "integer", nullable: false),
                    Item3 = table.Column<int>(type: "integer", nullable: false),
                    Item4 = table.Column<int>(type: "integer", nullable: false),
                    Item5 = table.Column<int>(type: "integer", nullable: false),
                    Item6 = table.Column<int>(type: "integer", nullable: false),
                    TrinketItemId = table.Column<int>(type: "integer", nullable: false),
                    PerksDefense = table.Column<int>(type: "integer", nullable: false),
                    PerksFlex = table.Column<int>(type: "integer", nullable: false),
                    PerksOffense = table.Column<int>(type: "integer", nullable: false),
                    PrimaryStyleId = table.Column<int>(type: "integer", nullable: false),
                    SubStyleId = table.Column<int>(type: "integer", nullable: false),
                    Summoner1Id = table.Column<int>(type: "integer", nullable: false),
                    Summoner2Id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_participants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "participant_perk_selections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    StyleId = table.Column<int>(type: "integer", nullable: false),
                    StyleDescription = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SelectionIndex = table.Column<int>(type: "integer", nullable: false),
                    PerkId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_participant_perk_selections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "participant_item_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    Puuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TimestampMs = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    BeforeId = table.Column<int>(type: "integer", nullable: true),
                    AfterId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_participant_item_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_participant_item_events_match_participants_MatchParticipant~",
                        column: x => x.MatchParticipantId,
                        principalTable: "match_participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "participant_skill_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    Puuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TimestampMs = table.Column<int>(type: "integer", nullable: false),
                    SkillSlot = table.Column<int>(type: "integer", nullable: false),
                    LevelUpType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_participant_skill_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_participant_skill_events_match_participants_MatchParticipan~",
                        column: x => x.MatchParticipantId,
                        principalTable: "match_participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_participant_item_events_MatchParticipantId",
                table: "participant_item_events",
                column: "MatchParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_participant_perk_selections_MatchId_ParticipantId_StyleId_S~",
                table: "participant_perk_selections",
                columns: new[] { "MatchId", "ParticipantId", "StyleId", "SelectionIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_participant_skill_events_MatchParticipantId",
                table: "participant_skill_events",
                column: "MatchParticipantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "participant_item_events");

            migrationBuilder.DropTable(
                name: "participant_perk_selections");

            migrationBuilder.DropTable(
                name: "participant_skill_events");

            migrationBuilder.DropTable(
                name: "match_participants");
        }
    }
}
