using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeedRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "seed_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TagLine = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PlatformId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedPuuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResolvedRiotAccountId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seed_requests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_seed_requests_RequestedAtUtc",
                table: "seed_requests",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_seed_requests_Status",
                table: "seed_requests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "seed_requests");
        }
    }
}
