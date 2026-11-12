using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class CardCodeRefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fotografia do card sem plano (itens de trabalho avulsos): PlanId nulo; FK e índice (PlanId, Ref) seguem, e a
            // unicidade dessas passa a ser por (card, ref) num índice parcial.
            migrationBuilder.AlterColumn<Guid>(
                name: "PlanId",
                schema: "execution",
                table: "ExecutionCodeRefs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionCodeRefs_CardNumber_Ref_NoPlan",
                schema: "execution",
                table: "ExecutionCodeRefs",
                columns: new[] { "CardNumber", "Ref" },
                unique: true,
                filter: "\"PlanId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // As fotografias do card sem plano não cabem no modelo antigo (PlanId obrigatório).
            migrationBuilder.Sql("DELETE FROM execution.\"ExecutionCodeRefs\" WHERE \"PlanId\" IS NULL;");

            migrationBuilder.DropIndex(
                name: "IX_ExecutionCodeRefs_CardNumber_Ref_NoPlan",
                schema: "execution",
                table: "ExecutionCodeRefs");

            migrationBuilder.AlterColumn<Guid>(
                name: "PlanId",
                schema: "execution",
                table: "ExecutionCodeRefs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
