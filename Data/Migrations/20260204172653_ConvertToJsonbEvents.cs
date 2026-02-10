using System;
using System.Collections.Generic;
using Data.Entities;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvertToJsonbEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "participant_item_events");

            migrationBuilder.DropTable(
                name: "participant_skill_events");

            migrationBuilder.AddColumn<List<ItemEvent>>(
                name: "ItemEvents",
                table: "match_participants",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<List<SkillEvent>>(
                name: "SkillEvents",
                table: "match_participants",
                type: "jsonb",
                nullable: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ItemEvents",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "SkillEvents",
                table: "match_participants");

            migrationBuilder.CreateTable(
                name: "participant_item_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AfterId = table.Column<int>(type: "integer", nullable: true),
                    BeforeId = table.Column<int>(type: "integer", nullable: true),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    Puuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TimestampMs = table.Column<int>(type: "integer", nullable: false)
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
                    LevelUpType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    Puuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SkillSlot = table.Column<int>(type: "integer", nullable: false),
                    TimestampMs = table.Column<int>(type: "integer", nullable: false)
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
                name: "IX_participant_skill_events_MatchParticipantId",
                table: "participant_skill_events",
                column: "MatchParticipantId");
        }
    }
}
