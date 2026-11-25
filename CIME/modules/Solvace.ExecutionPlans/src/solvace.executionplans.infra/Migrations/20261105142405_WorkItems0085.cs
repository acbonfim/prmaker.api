using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class WorkItems0085 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExecutionRequests_ActiveCard",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.CreateSequence(
                name: "work_item_number_seq",
                schema: "execution",
                maxValue: 9999999L);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkItemId",
                schema: "execution",
                table: "ExecutionRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkItems",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('execution.work_item_number_seq')"),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActiveRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Links = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemLogs",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskNumber = table.Column<long>(type: "bigint", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemLogs_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalSchema: "execution",
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemTasks",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('execution.work_item_number_seq')"),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Executor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Params = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StepKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Result = table.Column<string>(type: "text", nullable: true),
                    Output = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    Undo = table.Column<string>(type: "jsonb", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemTasks_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalSchema: "execution",
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRequests_ActiveCard",
                schema: "execution",
                table: "ExecutionRequests",
                column: "CardNumber",
                unique: true,
                filter: "\"Status\" IN ('queued', 'claimed', 'running') AND \"Kind\" NOT IN ('specialist', 'work-item', 'work-item-undo')");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRequests_ActiveWorkItem",
                schema: "execution",
                table: "ExecutionRequests",
                column: "WorkItemId",
                unique: true,
                filter: "\"Status\" IN ('queued', 'claimed', 'running') AND \"Kind\" IN ('work-item', 'work-item-undo')");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemLogs_WorkItemId_Id",
                schema: "execution",
                table: "WorkItemLogs",
                columns: new[] { "WorkItemId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_CardNumber",
                schema: "execution",
                table: "WorkItems",
                column: "CardNumber");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_CardNumber_Fingerprint",
                schema: "execution",
                table: "WorkItems",
                columns: new[] { "CardNumber", "Fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_Number",
                schema: "execution",
                table: "WorkItems",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_PlanId",
                schema: "execution",
                table: "WorkItems",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemTasks_Number",
                schema: "execution",
                table: "WorkItemTasks",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemTasks_WorkItemId_Order",
                schema: "execution",
                table: "WorkItemTasks",
                columns: new[] { "WorkItemId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkItemLogs",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "WorkItemTasks",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "WorkItems",
                schema: "execution");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionRequests_ActiveCard",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionRequests_ActiveWorkItem",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropColumn(
                name: "WorkItemId",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropSequence(
                name: "work_item_number_seq",
                schema: "execution");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRequests_ActiveCard",
                schema: "execution",
                table: "ExecutionRequests",
                column: "CardNumber",
                unique: true,
                filter: "\"Status\" IN ('queued', 'claimed', 'running') AND \"Kind\" <> 'specialist'");
        }
    }
}
