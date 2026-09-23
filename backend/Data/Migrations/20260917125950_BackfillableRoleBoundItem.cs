using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillableRoleBoundItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dropped and re-added rather than altered: every existing row must become
            // NULL ("never recorded") so the backfill can tell it from a real empty
            // slot, and an UPDATE would rewrite the whole table where a nullable
            // ADD COLUMN is a catalog change. The only values lost are the ones
            // preprod recorded since AddMatchParticipantRoleBoundItem; the backfill
            // recovers the bot laners among them. On prod both migrations ship in
            // the same release, so nothing was ever recorded there.
            migrationBuilder.DropColumn(
                name: "RoleBoundItemId",
                table: "match_participants");

            migrationBuilder.AddColumn<int>(
                name: "RoleBoundItemId",
                table: "match_participants",
                type: "integer",
                nullable: true);

            // Plain, transactional build, like IX_match_participants_champion_position_full:
            // one heap pass under a SHARE lock, reproducible from the migrations alone.
            // It starts with every legacy bot-lane row and shrinks as the backfill drains.
            migrationBuilder.CreateIndex(
                name: "IX_match_participants_role_bound_pending",
                table: "match_participants",
                column: "Id",
                filter: "\"RoleBoundItemId\" IS NULL AND \"TeamPosition\" = 'BOTTOM'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_match_participants_role_bound_pending",
                table: "match_participants");

            migrationBuilder.Sql(
                "UPDATE match_participants SET \"RoleBoundItemId\" = 0 WHERE \"RoleBoundItemId\" IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "RoleBoundItemId",
                table: "match_participants",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
