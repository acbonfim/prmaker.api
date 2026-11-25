using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromotionBatches",
                schema: "prform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromotionItems",
                schema: "prform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CardTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceBase = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TargetTemplate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TargetBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Draft = table.Column<bool>(type: "boolean", nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NewBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConflictFiles = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CommitCount = table.Column<int>(type: "integer", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "integer", nullable: true),
                    PullRequestUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PullRequestGithubId = table.Column<int>(type: "integer", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    WorkerId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionItems_PromotionBatches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "prform",
                        principalTable: "PromotionBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionBatches_OwnerUserId_CreatedAt",
                schema: "prform",
                table: "PromotionBatches",
                columns: new[] { "OwnerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionItems_BatchId_Position",
                schema: "prform",
                table: "PromotionItems",
                columns: new[] { "BatchId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionItems_CardNumber_RepositoryId_TargetBranch",
                schema: "prform",
                table: "PromotionItems",
                columns: new[] { "CardNumber", "RepositoryId", "TargetBranch" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionItems_Status_WorkerId",
                schema: "prform",
                table: "PromotionItems",
                columns: new[] { "Status", "WorkerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromotionItems",
                schema: "prform");

            migrationBuilder.DropTable(
                name: "PromotionBatches",
                schema: "prform");
        }
    }
}
