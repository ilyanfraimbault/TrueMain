using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRejectedCandidateStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MainCandidateStatus.Rejected (stored as 5) is removed (#1029): a candidate is in
            // the pipeline, demoted back into the pool, or pruned — there is no rejection
            // verdict, and no process ever assigned one. Any row still carrying the value would
            // no longer map to a defined status, so it goes back to Scored, the pool a demotion
            // returns to; the stale prune then treats it like any other never-promoted row.
            // Nothing ever wrote the value, so on every environment this touches zero rows.
            migrationBuilder.Sql("""
                UPDATE main_candidates SET "Status" = 1 WHERE "Status" = 5;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the rows this moved cannot be told apart from other Scored rows,
            // and the status they carried no longer exists.
        }
    }
}
