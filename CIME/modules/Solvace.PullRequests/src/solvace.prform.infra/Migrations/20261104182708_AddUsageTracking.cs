using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsageEvents",
                schema: "prform",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserExternalId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Screen = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Route = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Card = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Detail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageSessions",
                schema: "prform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserExternalId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Browser = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Os = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Device = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ScreenSize = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Language = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    AppVersion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    LastScreen = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    LastRoute = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ForegroundMs = table.Column<long>(type: "bigint", nullable: false),
                    BackgroundMs = table.Column<long>(type: "bigint", nullable: false),
                    AwayMs = table.Column<long>(type: "bigint", nullable: false),
                    Views = table.Column<int>(type: "integer", nullable: false),
                    Actions = table.Column<int>(type: "integer", nullable: false),
                    Events = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_At",
                schema: "prform",
                table: "UsageEvents",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_SessionId_At",
                schema: "prform",
                table: "UsageEvents",
                columns: new[] { "SessionId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_UserExternalId_At",
                schema: "prform",
                table: "UsageEvents",
                columns: new[] { "UserExternalId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageSessions_LastSeenAt",
                schema: "prform",
                table: "UsageSessions",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_UsageSessions_UserExternalId_LastSeenAt",
                schema: "prform",
                table: "UsageSessions",
                columns: new[] { "UserExternalId", "LastSeenAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsageEvents",
                schema: "prform");

            migrationBuilder.DropTable(
                name: "UsageSessions",
                schema: "prform");
        }
    }
}
