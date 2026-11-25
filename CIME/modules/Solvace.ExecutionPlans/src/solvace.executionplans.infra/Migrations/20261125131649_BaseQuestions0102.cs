using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class BaseQuestions0102 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BaseQuestions",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Question = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Deep = table.Column<bool>(type: "boolean", nullable: false),
                    Files = table.Column<string>(type: "text", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Answer = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaseQuestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BaseQuestions_Key",
                schema: "execution",
                table: "BaseQuestions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BaseQuestions_OwnerUserId_CreatedAt",
                schema: "execution",
                table: "BaseQuestions",
                columns: new[] { "OwnerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BaseQuestions_RequestId",
                schema: "execution",
                table: "BaseQuestions",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BaseQuestions",
                schema: "execution");
        }
    }
}
