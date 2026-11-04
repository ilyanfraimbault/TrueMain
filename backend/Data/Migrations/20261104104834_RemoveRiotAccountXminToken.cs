using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRiotAccountXminToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Model only (#2448): xmin is a Postgres system column, never a real one, so taking
            // the token off RiotAccount changes no schema. Scaffolded as a DropColumn, which the
            // Npgsql generator skips on system columns; left empty so it reads as what it is.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
