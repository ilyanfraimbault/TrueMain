using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizePerkSelectionCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "perk_selection_catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StyleId = table.Column<int>(type: "integer", nullable: false),
                    SelectionIndex = table.Column<int>(type: "integer", nullable: false),
                    PerkId = table.Column<int>(type: "integer", nullable: false),
                    StyleDescription = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perk_selection_catalog", x => x.Id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO perk_selection_catalog ("StyleId", "SelectionIndex", "PerkId", "StyleDescription")
                SELECT DISTINCT "StyleId", "SelectionIndex", "PerkId", "StyleDescription"
                FROM participant_perk_selections;
                """);

            migrationBuilder.AddColumn<int>(
                name: "PerkSelectionCatalogId",
                table: "participant_perk_selections",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE participant_perk_selections p
                SET "PerkSelectionCatalogId" = c."Id"
                FROM perk_selection_catalog c
                WHERE p."StyleId" = c."StyleId"
                  AND p."SelectionIndex" = c."SelectionIndex"
                  AND p."PerkId" = c."PerkId"
                  AND p."StyleDescription" = c."StyleDescription";
                """);

            migrationBuilder.DropIndex(
                name: "IX_participant_perk_selections_MatchId_ParticipantId_StyleId_S~",
                table: "participant_perk_selections");

            migrationBuilder.AlterColumn<int>(
                name: "PerkSelectionCatalogId",
                table: "participant_perk_selections",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_participant_perk_selections_MatchId_ParticipantId_PerkSelec~",
                table: "participant_perk_selections",
                columns: new[] { "MatchId", "ParticipantId", "PerkSelectionCatalogId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_participant_perk_selections_PerkSelectionCatalogId",
                table: "participant_perk_selections",
                column: "PerkSelectionCatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_perk_selection_catalog_StyleId_SelectionIndex_PerkId_StyleD~",
                table: "perk_selection_catalog",
                columns: new[] { "StyleId", "SelectionIndex", "PerkId", "StyleDescription" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_participant_perk_selections_perk_selection_catalog_PerkSele~",
                table: "participant_perk_selections",
                column: "PerkSelectionCatalogId",
                principalTable: "perk_selection_catalog",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropColumn(
                name: "StyleId",
                table: "participant_perk_selections");

            migrationBuilder.DropColumn(
                name: "SelectionIndex",
                table: "participant_perk_selections");

            migrationBuilder.DropColumn(
                name: "PerkId",
                table: "participant_perk_selections");

            migrationBuilder.DropColumn(
                name: "StyleDescription",
                table: "participant_perk_selections");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_participant_perk_selections_perk_selection_catalog_PerkSele~",
                table: "participant_perk_selections");

            migrationBuilder.DropIndex(
                name: "IX_participant_perk_selections_MatchId_ParticipantId_PerkSelec~",
                table: "participant_perk_selections");

            migrationBuilder.DropIndex(
                name: "IX_participant_perk_selections_PerkSelectionCatalogId",
                table: "participant_perk_selections");

            migrationBuilder.AddColumn<int>(
                name: "StyleId",
                table: "participant_perk_selections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SelectionIndex",
                table: "participant_perk_selections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PerkId",
                table: "participant_perk_selections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StyleDescription",
                table: "participant_perk_selections",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE participant_perk_selections p
                SET "StyleId" = c."StyleId",
                    "SelectionIndex" = c."SelectionIndex",
                    "PerkId" = c."PerkId",
                    "StyleDescription" = c."StyleDescription"
                FROM perk_selection_catalog c
                WHERE p."PerkSelectionCatalogId" = c."Id";
                """);

            migrationBuilder.AlterColumn<int>(
                name: "StyleId",
                table: "participant_perk_selections",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SelectionIndex",
                table: "participant_perk_selections",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PerkId",
                table: "participant_perk_selections",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StyleDescription",
                table: "participant_perk_selections",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_participant_perk_selections_MatchId_ParticipantId_StyleId_S~",
                table: "participant_perk_selections",
                columns: new[] { "MatchId", "ParticipantId", "StyleId", "SelectionIndex" },
                unique: true);

            migrationBuilder.DropColumn(
                name: "PerkSelectionCatalogId",
                table: "participant_perk_selections");

            migrationBuilder.DropTable(
                name: "perk_selection_catalog");
        }
    }
}
