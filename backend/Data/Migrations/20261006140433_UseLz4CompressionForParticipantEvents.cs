using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Switches the TOAST compression of <c>match_participants."ItemEvents"</c> and
    /// <c>"SkillEvents"</c> from the cluster default (PGLZ) to LZ4 (#123).
    ///
    /// <para>
    /// <c>SET COMPRESSION</c> is a catalog-only change: it takes a brief
    /// <c>ACCESS EXCLUSIVE</c> lock, rewrites nothing, and only applies to values written
    /// afterwards. Existing rows keep PGLZ until the table is rewritten, which the
    /// <c>match_participants</c> repack of #1946 does — no rewrite belongs here (CLAUDE.md:
    /// startup and script migrations stay fast).
    /// </para>
    ///
    /// <para>
    /// <b>Why raw SQL.</b> The model carries the setting
    /// (<c>UseCompressionMethod("lz4")</c>, so the snapshot and compiled model know it), but
    /// the scaffolded <c>AlterColumn</c> is not used: Npgsql's SQL generator emits
    /// <c>SET COMPRESSION</c> without a terminating semicolon, which the idempotent script
    /// turns into <c>... lz4 END IF;</c> inside EF's <c>DO $EF$</c> block — a syntax error
    /// on the <c>migrate</c> job's path.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class UseLz4CompressionForParticipantEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE match_participants ALTER COLUMN "ItemEvents" SET COMPRESSION lz4;
                ALTER TABLE match_participants ALTER COLUMN "SkillEvents" SET COMPRESSION lz4;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE match_participants ALTER COLUMN "ItemEvents" SET COMPRESSION default;
                ALTER TABLE match_participants ALTER COLUMN "SkillEvents" SET COMPRESSION default;
                """);
        }
    }
}
