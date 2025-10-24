using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCardFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardNumber",
                table: "PullRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RootCause",
                table: "PullRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardNumber",
                table: "PullRequests");

            migrationBuilder.DropColumn(
                name: "RootCause",
                table: "PullRequests");
        }
    }
}
