using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMainCandidateListOrderIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_main_candidates_list_order",
                table: "main_candidates",
                columns: new[] { "Score", "DiscoveredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_main_candidates_status_list_order",
                table: "main_candidates",
                columns: new[] { "Status", "Score", "DiscoveredAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_main_candidates_list_order",
                table: "main_candidates");

            migrationBuilder.DropIndex(
                name: "IX_main_candidates_status_list_order",
                table: "main_candidates");
        }
    }
}
