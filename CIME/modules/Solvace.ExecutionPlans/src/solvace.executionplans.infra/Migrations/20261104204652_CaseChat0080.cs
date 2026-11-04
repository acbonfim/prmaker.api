using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class CaseChat0080 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExecutionRequests_ActiveCard",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.AddColumn<bool>(
                name: "Deep",
                schema: "execution",
                table: "ExecutionRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DivergenceAt",
                schema: "execution",
                table: "ExecutionPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DivergenceConfirmedAt",
                schema: "execution",
                table: "ExecutionPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DivergenceConfirmedBy",
                schema: "execution",
                table: "ExecutionPlans",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DivergenceStepKey",
                schema: "execution",
                table: "ExecutionPlans",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DivergenceText",
                schema: "execution",
                table: "ExecutionPlans",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CaseActions",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Detail = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Result = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DoneAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CaseChats",
                schema: "execution",
                columns: table => new
                {
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SessionHost = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SessionCwd = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastReadNumber = table.Column<int>(type: "integer", nullable: false),
                    PendingRun = table.Column<bool>(type: "boolean", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseChats", x => x.CardNumber);
                });

            migrationBuilder.CreateTable(
                name: "CaseFiles",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FromSpecialist = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CaseMessages",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    AuthorKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Transcript = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    PeopleOnly = table.Column<bool>(type: "boolean", nullable: false),
                    Deep = table.Column<bool>(type: "boolean", nullable: false),
                    Mentions = table.Column<string>(type: "jsonb", nullable: false),
                    Steps = table.Column<string>(type: "jsonb", nullable: false),
                    ReplyToNumber = table.Column<int>(type: "integer", nullable: true),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CasePoints",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MessageNumber = table.Column<int>(type: "integer", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FromSpecialist = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CasePoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CaseProposals",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Phase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Execute = table.Column<bool>(type: "boolean", nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PlanNoteNumber = table.Column<int>(type: "integer", nullable: true),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Changes = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CaseFileContents",
                schema: "execution",
                columns: table => new
                {
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseFileContents", x => x.FileId);
                    table.ForeignKey(
                        name: "FK_CaseFileContents_CaseFiles_FileId",
                        column: x => x.FileId,
                        principalSchema: "execution",
                        principalTable: "CaseFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRequests_ActiveCard",
                schema: "execution",
                table: "ExecutionRequests",
                column: "CardNumber",
                unique: true,
                filter: "\"Status\" IN ('queued', 'claimed', 'running') AND \"Kind\" <> 'specialist'");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRequests_ActiveCaseCard",
                schema: "execution",
                table: "ExecutionRequests",
                column: "CardNumber",
                unique: true,
                filter: "\"Status\" IN ('queued', 'claimed', 'running') AND \"Kind\" = 'specialist'");

            migrationBuilder.CreateIndex(
                name: "IX_CaseActions_CardNumber_Number",
                schema: "execution",
                table: "CaseActions",
                columns: new[] { "CardNumber", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseFiles_CardNumber_Number",
                schema: "execution",
                table: "CaseFiles",
                columns: new[] { "CardNumber", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseFiles_MessageId",
                schema: "execution",
                table: "CaseFiles",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseMessages_CardNumber_Number",
                schema: "execution",
                table: "CaseMessages",
                columns: new[] { "CardNumber", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseMessages_RequestId",
                schema: "execution",
                table: "CaseMessages",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_CasePoints_CardNumber",
                schema: "execution",
                table: "CasePoints",
                column: "CardNumber");

            migrationBuilder.CreateIndex(
                name: "IX_CaseProposals_CardNumber_Number",
                schema: "execution",
                table: "CaseProposals",
                columns: new[] { "CardNumber", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseActions",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "CaseChats",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "CaseFileContents",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "CaseMessages",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "CasePoints",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "CaseProposals",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "CaseFiles",
                schema: "execution");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionRequests_ActiveCard",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionRequests_ActiveCaseCard",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropColumn(
                name: "Deep",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropColumn(
                name: "DivergenceAt",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "DivergenceConfirmedAt",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "DivergenceConfirmedBy",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "DivergenceStepKey",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.DropColumn(
                name: "DivergenceText",
                schema: "execution",
                table: "ExecutionPlans");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRequests_ActiveCard",
                schema: "execution",
                table: "ExecutionRequests",
                column: "CardNumber",
                unique: true,
                filter: "\"Status\" IN ('queued', 'claimed', 'running')");
        }
    }
}
