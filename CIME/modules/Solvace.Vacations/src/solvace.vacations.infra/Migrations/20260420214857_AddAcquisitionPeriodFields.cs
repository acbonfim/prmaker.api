using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.vacations.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddAcquisitionPeriodFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcquisitionPeriodEnd",
                table: "UserVacationBalances",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "AcquisitionPeriodStart",
                table: "UserVacationBalances",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UsagePeriodEnd",
                table: "UserVacationBalances",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UsagePeriodStart",
                table: "UserVacationBalances",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcquisitionPeriodEnd",
                table: "UserVacationBalances");

            migrationBuilder.DropColumn(
                name: "AcquisitionPeriodStart",
                table: "UserVacationBalances");

            migrationBuilder.DropColumn(
                name: "UsagePeriodEnd",
                table: "UserVacationBalances");

            migrationBuilder.DropColumn(
                name: "UsagePeriodStart",
                table: "UserVacationBalances");
        }
    }
}
