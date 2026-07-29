using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchBans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing matches must be flagged as already folded. Not the #811
            // double-counting guard — the aggregate tables below are created empty —
            // but a correctness one: Riot payloads are not retained, so bans could not
            // be backfilled and a pre-#920 match has no match_bans rows at all. Folding
            // it would add one to ban_scope_totals while contributing no
            // champion_ban_stats, i.e. silently deflate every champion's ban rate for
            // as long as those matches are retained. Ban history therefore starts at
            // deploy, and the read surfaces the gap instead of a zero.
            //
            // Done as add-with-default-true then drop-the-default rather than
            // AddChampionMatchupLeadAggregatedFlag's `UPDATE matches SET ... = true`:
            // both statements below are catalog-only on PG 11+ (the default is recorded
            // as the attribute's missing-value, existing rows read it without being
            // touched), whereas the UPDATE rewrites every row of the largest table in
            // the database. Prod applies migrations on startup, so a rewrite there is a
            // command-timeout crash-loop risk (see CLAUDE.md). The partial index is
            // created afterwards so it is built over the empty pending set.
            migrationBuilder.AddColumn<bool>(
                name: "BansAggregated",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Newly-ingested matches must arrive pending, so the column default flips
            // back to false once the existing rows have their value.
            migrationBuilder.AlterColumn<bool>(
                name: "BansAggregated",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: false,
                oldDefaultValue: true);

            migrationBuilder.CreateTable(
                name: "ban_scope_totals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    elo_bracket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    Matches = table.Column<int>(type: "integer", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ban_scope_totals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "champion_ban_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    elo_bracket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    Bans = table.Column<int>(type: "integer", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_ban_stats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "match_bans",
                columns: table => new
                {
                    MatchId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    PickTurn = table.Column<int>(type: "integer", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_bans", x => new { x.MatchId, x.TeamId, x.PickTurn });
                    table.ForeignKey(
                        name: "FK_match_bans_matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_bans_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"BansAggregated\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ban_scope_totals_grain",
                table: "ban_scope_totals",
                columns: new[] { "Patch", "elo_bracket" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_ban_stats_grain",
                table: "champion_ban_stats",
                columns: new[] { "Patch", "elo_bracket", "ChampionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ban_scope_totals");

            migrationBuilder.DropTable(
                name: "champion_ban_stats");

            migrationBuilder.DropTable(
                name: "match_bans");

            migrationBuilder.DropIndex(
                name: "IX_matches_bans_pending",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "BansAggregated",
                table: "matches");
        }
    }
}
