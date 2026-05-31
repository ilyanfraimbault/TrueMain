using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRiotAccountScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "riot_accounts",
                type: "integer",
                nullable: true);

            // One-shot backfill from each account's latest rank snapshot, using
            // the same coefficients as Core.Lol.Ranking.RankScore (the hot path
            // no longer computes this in SQL). Accounts with no snapshot stay NULL.
            migrationBuilder.Sql("""
                UPDATE riot_accounts AS a
                SET "Score" = sub.score
                FROM (
                    SELECT DISTINCT ON (rs."RiotAccountId")
                        rs."RiotAccountId" AS account_id,
                        ((CASE rs."Tier"
                            WHEN 'IRON' THEN 0 WHEN 'BRONZE' THEN 1 WHEN 'SILVER' THEN 2
                            WHEN 'GOLD' THEN 3 WHEN 'PLATINUM' THEN 4 WHEN 'EMERALD' THEN 5
                            WHEN 'DIAMOND' THEN 6 WHEN 'MASTER' THEN 7 WHEN 'GRANDMASTER' THEN 7
                            WHEN 'CHALLENGER' THEN 7 ELSE NULL END) * 400
                         + (CASE WHEN rs."Tier" IN ('MASTER', 'GRANDMASTER', 'CHALLENGER') THEN 0
                              ELSE (CASE rs."Division"
                                WHEN 'I' THEN 3 WHEN 'II' THEN 2 WHEN 'III' THEN 1 WHEN 'IV' THEN 0
                                ELSE 0 END) END) * 100
                         + rs."LeaguePoints") AS score
                    FROM rank_snapshots rs
                    ORDER BY rs."RiotAccountId", rs."CapturedAtUtc" DESC
                ) AS sub
                WHERE a."Id" = sub.account_id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_riot_accounts_score",
                table: "riot_accounts",
                column: "Score");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_riot_accounts_score",
                table: "riot_accounts");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "riot_accounts");
        }
    }
}
