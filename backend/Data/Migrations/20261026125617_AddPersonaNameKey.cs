using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonaNameKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "personas",
                type: "text",
                nullable: false,
                computedColumnSql: "regexp_replace(lower(\"DisplayName\"), '[[:space:]._-]+', '', 'g')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_personas_NameKey",
                table: "personas",
                column: "NameKey",
                filter: "\"MergedIntoPersonaId\" IS NULL AND \"RemovedAtUtc\" IS NULL")
                .Annotation("Npgsql:IndexInclude", new[] { "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_personas_NameKey",
                table: "personas");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "personas");
        }
    }
}
