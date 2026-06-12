using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessRunIterationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IterationId",
                table: "process_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_process_runs_IterationId_StartedAtUtc",
                table: "process_runs",
                columns: new[] { "IterationId", "StartedAtUtc" },
                filter: "\"IterationId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_process_runs_IterationId_StartedAtUtc",
                table: "process_runs");

            migrationBuilder.DropColumn(
                name: "IterationId",
                table: "process_runs");
        }
    }
}
