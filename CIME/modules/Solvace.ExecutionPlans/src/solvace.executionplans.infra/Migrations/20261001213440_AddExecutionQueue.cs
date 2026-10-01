using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutionRequests",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SessionHost = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SessionCwd = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TargetWorkerId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkerId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Force = table.Column<bool>(type: "boolean", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    NotBefore = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Pid = table.Column<int>(type: "integer", nullable: true),
                    WaitReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    StderrTail = table.Column<string>(type: "text", nullable: true),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    FinishedReason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FinishedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CostUsd = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    Turns = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionUserSettings",
                schema: "execution",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DailyBudgetUsd = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    AutoAnalyzeEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AutoWorkItemTypes = table.Column<List<string>>(type: "text[]", nullable: false),
                    AutoStates = table.Column<List<string>>(type: "text[]", nullable: false),
                    AutoAreaPaths = table.Column<List<string>>(type: "text[]", nullable: false),
                    AutoAssignedTo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AutoMaxPerDay = table.Column<int>(type: "integer", nullable: false),
                    AutoLastCheckAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AutoLastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionUserSettings", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionWorkers",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Os = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AgentVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ClaudeVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SkillsVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Workspace = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MaxConcurrency = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CredentialId = table.Column<Guid>(type: "uuid", nullable: false),
                    Capabilities = table.Column<string>(type: "jsonb", nullable: true),
                    Doctor = table.Column<string>(type: "jsonb", nullable: true),
                    DoctorAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionWorkers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRequests_ActiveCard",
                schema: "execution",
                table: "ExecutionRequests",
                column: "CardNumber",
                unique: true,
                filter: "\"Status\" IN ('queued', 'claimed', 'running')");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRequests_CardNumber_CreatedAt",
                schema: "execution",
                table: "ExecutionRequests",
                columns: new[] { "CardNumber", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRequests_OwnerUserId_Status",
                schema: "execution",
                table: "ExecutionRequests",
                columns: new[] { "OwnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionRequests_WorkerId_Status",
                schema: "execution",
                table: "ExecutionRequests",
                columns: new[] { "WorkerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionWorkers_OwnerUserId_Host",
                schema: "execution",
                table: "ExecutionWorkers",
                columns: new[] { "OwnerUserId", "Host" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionRequests",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "ExecutionUserSettings",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "ExecutionWorkers",
                schema: "execution");
        }
    }
}
