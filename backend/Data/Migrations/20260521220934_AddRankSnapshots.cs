using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRankSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rank_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RiotAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    Tier = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Division = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    LeaguePoints = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: true),
                    Losses = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rank_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rank_snapshots_riot_accounts_RiotAccountId",
                        column: x => x.RiotAccountId,
                        principalTable: "riot_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rank_snapshots_account_captured",
                table: "rank_snapshots",
                columns: new[] { "RiotAccountId", "CapturedAtUtc" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rank_snapshots");
        }
    }
}
