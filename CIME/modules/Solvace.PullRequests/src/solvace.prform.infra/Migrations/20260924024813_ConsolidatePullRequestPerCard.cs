using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidatePullRequestPerCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Consolida os registros por card (antes havia um por card x repositório) para
            // permitir o índice único em CardNumber. Mantém a linha mais recente
            // (UpdatedAt ?? CreatedAt, desempate pelo maior Id) e copia para ela o
            // Description/RootCause mais recente não vazio entre as linhas do card.
            // As tabelas derivadas com ROW_NUMBER são materializadas (evita o erro 1093 do MySQL).
            migrationBuilder.Sql("UPDATE PullRequests SET CardNumber = TRIM(CardNumber);");

            migrationBuilder.Sql(@"
UPDATE PullRequests k
JOIN (SELECT Id, ROW_NUMBER() OVER (PARTITION BY CardNumber ORDER BY COALESCE(UpdatedAt, CreatedAt) DESC, Id DESC) AS rn
      FROM PullRequests) keep ON keep.Id = k.Id AND keep.rn = 1
JOIN (SELECT CardNumber, RootCause,
             ROW_NUMBER() OVER (PARTITION BY CardNumber ORDER BY (RootCause <> '') DESC, COALESCE(UpdatedAt, CreatedAt) DESC, Id DESC) AS rn
      FROM PullRequests) rc ON rc.CardNumber = k.CardNumber AND rc.rn = 1
JOIN (SELECT CardNumber, Description,
             ROW_NUMBER() OVER (PARTITION BY CardNumber ORDER BY (Description <> '') DESC, COALESCE(UpdatedAt, CreatedAt) DESC, Id DESC) AS rn
      FROM PullRequests) ds ON ds.CardNumber = k.CardNumber AND ds.rn = 1
SET k.RootCause = rc.RootCause,
    k.Description = ds.Description;");

            migrationBuilder.Sql(@"
DELETE p FROM PullRequests p
JOIN (SELECT Id, ROW_NUMBER() OVER (PARTITION BY CardNumber ORDER BY COALESCE(UpdatedAt, CreatedAt) DESC, Id DESC) AS rn
      FROM PullRequests) d ON d.Id = p.Id
WHERE d.rn > 1;");

            migrationBuilder.AlterColumn<string>(
                name: "CardNumber",
                table: "PullRequests",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "BranchPrefix",
                table: "PullRequests",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "BranchName",
                table: "PullRequests",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_CardNumber",
                table: "PullRequests",
                column: "CardNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A consolidação dos registros por card (Up) não é reversível: só o schema é revertido.
            migrationBuilder.DropIndex(
                name: "IX_PullRequests_CardNumber",
                table: "PullRequests");

            migrationBuilder.AlterColumn<string>(
                name: "CardNumber",
                table: "PullRequests",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldMaxLength: 50)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "PullRequests",
                keyColumn: "BranchPrefix",
                keyValue: null,
                column: "BranchPrefix",
                value: "");

            migrationBuilder.AlterColumn<string>(
                name: "BranchPrefix",
                table: "PullRequests",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "PullRequests",
                keyColumn: "BranchName",
                keyValue: null,
                column: "BranchName",
                value: "");

            migrationBuilder.AlterColumn<string>(
                name: "BranchName",
                table: "PullRequests",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
