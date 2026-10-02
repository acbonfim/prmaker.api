using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <summary>
    /// 0044: partes da entrada, modelo e o acumulado da sessão por pedido. O Claude Code informa o custo ACUMULADO da
    /// sessão retomada — os pedidos já gravados passam a guardar esse acumulado em <c>SessionCostUsd</c> e o
    /// <c>CostUsd</c> vira a diferença para o pedido anterior da mesma sessão (o orçamento do dia também somava errado).
    /// </summary>
    public partial class ExecutionUsageBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CacheReadTokens",
                schema: "execution",
                table: "ExecutionRequests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CacheWriteTokens",
                schema: "execution",
                table: "ExecutionRequests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FreshInputTokens",
                schema: "execution",
                table: "ExecutionRequests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                schema: "execution",
                table: "ExecutionRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SessionCostUsd",
                schema: "execution",
                table: "ExecutionRequests",
                type: "numeric(12,4)",
                precision: 12,
                scale: 4,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE execution."ExecutionRequests" SET "SessionCostUsd" = "CostUsd" WHERE "CostUsd" IS NOT NULL;

                UPDATE execution."ExecutionRequests" r
                SET "CostUsd" = CASE WHEN x.prev IS NOT NULL AND r."SessionCostUsd" >= x.prev THEN r."SessionCostUsd" - x.prev ELSE r."SessionCostUsd" END
                FROM (
                    SELECT "Id", MAX("SessionCostUsd") OVER (PARTITION BY "SessionId" ORDER BY COALESCE("FinishedAt", "UpdatedAt"), "CreatedAt"
                                                             ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS prev
                    FROM execution."ExecutionRequests"
                    WHERE "SessionId" IS NOT NULL AND "SessionCostUsd" IS NOT NULL
                ) x
                WHERE x."Id" = r."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE execution."ExecutionRequests" SET "CostUsd" = "SessionCostUsd" WHERE "SessionCostUsd" IS NOT NULL;""");

            migrationBuilder.DropColumn(
                name: "CacheReadTokens",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropColumn(
                name: "CacheWriteTokens",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropColumn(
                name: "FreshInputTokens",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropColumn(
                name: "Model",
                schema: "execution",
                table: "ExecutionRequests");

            migrationBuilder.DropColumn(
                name: "SessionCostUsd",
                schema: "execution",
                table: "ExecutionRequests");
        }
    }
}
