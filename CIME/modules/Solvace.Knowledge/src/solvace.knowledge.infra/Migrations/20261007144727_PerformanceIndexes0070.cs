using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceIndexes0070 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ReverseIndexEntries_Kind",
                schema: "knowledge",
                table: "ReverseIndexEntries",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_ReverseIndexEntries_UpdatedAt",
                schema: "knowledge",
                table: "ReverseIndexEntries",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReverseIndexEntries_Kind",
                schema: "knowledge",
                table: "ReverseIndexEntries");

            migrationBuilder.DropIndex(
                name: "IX_ReverseIndexEntries_UpdatedAt",
                schema: "knowledge",
                table: "ReverseIndexEntries");
        }
    }
}
