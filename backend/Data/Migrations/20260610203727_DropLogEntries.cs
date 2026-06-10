using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class DropLogEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "log_entries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "log_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EventId = table.Column<int>(type: "integer", nullable: true),
                    Exception = table.Column<string>(type: "text", nullable: true),
                    Host = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    ProcessName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_log_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_log_entries_Category",
                table: "log_entries",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_log_entries_Level",
                table: "log_entries",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_log_entries_TimestampUtc",
                table: "log_entries",
                column: "TimestampUtc",
                descending: new bool[0]);
        }
    }
}
