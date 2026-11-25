using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.governance.infra.Migrations
{
    /// <inheritdoc />
    public partial class Standards0101 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GovernanceStandardDrafts",
                schema: "governance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Items = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AppliedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceStandardDrafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceStandards",
                schema: "governance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Area = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Topic = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TextPt = table.Column<string>(type: "text", nullable: false),
                    TextEn = table.Column<string>(type: "text", nullable: true),
                    Examples = table.Column<string>(type: "jsonb", nullable: false),
                    AppliesTo = table.Column<string>(type: "jsonb", nullable: false),
                    Source = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Effect = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Check = table.Column<string>(type: "jsonb", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceStandards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceStandards_Area_Topic",
                schema: "governance",
                table: "GovernanceStandards",
                columns: new[] { "Area", "Topic" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceStandards_Key",
                schema: "governance",
                table: "GovernanceStandards",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GovernanceStandardDrafts",
                schema: "governance");

            migrationBuilder.DropTable(
                name: "GovernanceStandards",
                schema: "governance");
        }
    }
}
