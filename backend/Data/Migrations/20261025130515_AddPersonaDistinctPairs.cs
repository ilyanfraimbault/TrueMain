using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonaDistinctPairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "persona_distinct_pairs",
                columns: table => new
                {
                    PersonaId = table.Column<Guid>(type: "uuid", nullable: false),
                    OtherPersonaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persona_distinct_pairs", x => new { x.PersonaId, x.OtherPersonaId });
                    table.CheckConstraint("CK_persona_distinct_pairs_ordered", "\"PersonaId\" < \"OtherPersonaId\"");
                    table.ForeignKey(
                        name: "FK_persona_distinct_pairs_personas_OtherPersonaId",
                        column: x => x.OtherPersonaId,
                        principalTable: "personas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_persona_distinct_pairs_personas_PersonaId",
                        column: x => x.PersonaId,
                        principalTable: "personas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_persona_distinct_pairs_OtherPersonaId",
                table: "persona_distinct_pairs",
                column: "OtherPersonaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "persona_distinct_pairs");
        }
    }
}
