using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonaManualEdits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DisplayNameLocked",
                table: "personas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemovedAtUtc",
                table: "personas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTakedown",
                table: "persona_account_exclusions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "persona_link_exclusions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persona_link_exclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_persona_link_exclusions_personas_PersonaId",
                        column: x => x.PersonaId,
                        principalTable: "personas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_persona_link_exclusions_PersonaId_Url",
                table: "persona_link_exclusions",
                columns: new[] { "PersonaId", "Url" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "persona_link_exclusions");

            migrationBuilder.DropColumn(
                name: "DisplayNameLocked",
                table: "personas");

            migrationBuilder.DropColumn(
                name: "RemovedAtUtc",
                table: "personas");

            migrationBuilder.DropColumn(
                name: "IsTakedown",
                table: "persona_account_exclusions");
        }
    }
}
