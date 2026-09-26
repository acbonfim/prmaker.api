using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace solvace.vacations.infra.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vacations");

            migrationBuilder.CreateTable(
                name: "UserVacationBalances",
                schema: "vacations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailableDays = table.Column<int>(type: "integer", nullable: false),
                    UsedDays = table.Column<int>(type: "integer", nullable: false),
                    AcquisitionPeriodStart = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    AcquisitionPeriodEnd = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UsagePeriodStart = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UsagePeriodEnd = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserVacationBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VacationRequests",
                schema: "vacations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    BusinessDays = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ManagerNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    HRNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ApprovedByManagerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedByManagerAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AuthorizedByHRId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorizedByHRAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacationRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserVacationBalances_UserId",
                schema: "vacations",
                table: "UserVacationBalances",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserVacationBalances_UserId_AcquisitionPeriodStart_Acquisit~",
                schema: "vacations",
                table: "UserVacationBalances",
                columns: new[] { "UserId", "AcquisitionPeriodStart", "AcquisitionPeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserVacationBalances_UserId_UsagePeriodStart_UsagePeriodEnd",
                schema: "vacations",
                table: "UserVacationBalances",
                columns: new[] { "UserId", "UsagePeriodStart", "UsagePeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_VacationRequests_StartDate_EndDate",
                schema: "vacations",
                table: "VacationRequests",
                columns: new[] { "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_VacationRequests_UserId",
                schema: "vacations",
                table: "VacationRequests",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserVacationBalances",
                schema: "vacations");

            migrationBuilder.DropTable(
                name: "VacationRequests",
                schema: "vacations");
        }
    }
}
