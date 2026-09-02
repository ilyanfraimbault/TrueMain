using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Adds the stored generated column <c>matches."Patch"</c> and its two indexes
    /// (#1368), replacing the unindexable <c>GameVersion LIKE '16.17.%'</c> every
    /// champion read used to scan with.
    /// <para>
    /// Cost and locking, because this is not a startup migration (#598, and
    /// <c>docs/production-migrations.md</c>: preprod and prod run
    /// <c>ApplyMigrationsOnStartup: false</c> and apply the idempotent script
    /// out of band, before the images roll). Adding a STORED generated column
    /// rewrites the table under ACCESS EXCLUSIVE; at ~274 k rows that is seconds,
    /// and the two ordinary <c>CREATE INDEX</c> passes over the same table are
    /// seconds more. The rewrite already holds the strongest lock there is, so
    /// building the indexes CONCURRENTLY would buy nothing and cost the ability
    /// to run inside the script's transaction.
    /// </para>
    /// </remarks>
    public partial class AddMatchPatchGeneratedColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Patch",
                table: "matches",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                computedColumnSql: "((regexp_match(\"GameVersion\", '^[\\s.]*([+-]?[0-9]{1,9})[\\s]*\\.[\\s.]*([+-]?[0-9]{1,9})[\\s]*(\\.|$)'))[1])::int::text || '.' || ((regexp_match(\"GameVersion\", '^[\\s.]*([+-]?[0-9]{1,9})[\\s]*\\.[\\s.]*([+-]?[0-9]{1,9})[\\s]*(\\.|$)'))[2])::int::text",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_matches_patch_queue",
                table: "matches",
                columns: new[] { "Patch", "QueueId" });

            migrationBuilder.CreateIndex(
                name: "IX_matches_queue_patch_platform",
                table: "matches",
                columns: new[] { "QueueId", "Patch", "PlatformId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_matches_patch_queue",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "IX_matches_queue_patch_platform",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "Patch",
                table: "matches");
        }
    }
}
