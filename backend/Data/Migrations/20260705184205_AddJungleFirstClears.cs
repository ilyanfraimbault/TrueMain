using System;
using System.Collections.Generic;
using Data.Entities;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJungleFirstClears : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jungle_first_clears",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    Steps = table.Column<List<JungleClearStep>>(type: "jsonb", nullable: false),
                    FullClearTimeMs = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jungle_first_clears", x => x.Id);
                    table.ForeignKey(
                        name: "FK_jungle_first_clears_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_jungle_first_clears_MatchId_ParticipantId",
                table: "jungle_first_clears",
                columns: new[] { "MatchId", "ParticipantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jungle_first_clears");
        }
    }
}
