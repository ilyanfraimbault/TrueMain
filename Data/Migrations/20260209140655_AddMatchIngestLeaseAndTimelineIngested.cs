using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchIngestLeaseAndTimelineIngested : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MatchIngestClaimedAtUtc",
                table: "riot_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TimelineIngested",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_riot_accounts_ingest_claim_lease",
                table: "riot_accounts",
                columns: new[] { "MatchIngestStatus", "MatchIngestClaimedAtUtc", "LastMatchIngestAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_matches_timeline_ingested",
                table: "matches",
                column: "TimelineIngested");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_riot_accounts_ingest_claim_lease",
                table: "riot_accounts");

            migrationBuilder.DropIndex(
                name: "IX_matches_timeline_ingested",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "MatchIngestClaimedAtUtc",
                table: "riot_accounts");

            migrationBuilder.DropColumn(
                name: "TimelineIngested",
                table: "matches");
        }
    }
}
