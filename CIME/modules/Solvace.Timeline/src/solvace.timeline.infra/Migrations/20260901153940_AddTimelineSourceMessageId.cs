using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.timeline.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineSourceMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceMessageId",
                table: "TimelineEntries",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_SourceMessageId",
                table: "TimelineEntries",
                column: "SourceMessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TimelineEntries_SourceMessageId",
                table: "TimelineEntries");

            migrationBuilder.DropColumn(
                name: "SourceMessageId",
                table: "TimelineEntries");
        }
    }
}
