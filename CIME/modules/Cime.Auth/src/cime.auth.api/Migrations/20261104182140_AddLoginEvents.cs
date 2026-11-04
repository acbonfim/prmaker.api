using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace cime.auth.api.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoginEvents",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    At = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    UserExternalId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoginEvents_At",
                schema: "auth",
                table: "LoginEvents",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_LoginEvents_UserExternalId_At",
                schema: "auth",
                table: "LoginEvents",
                columns: new[] { "UserExternalId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoginEvents",
                schema: "auth");
        }
    }
}
