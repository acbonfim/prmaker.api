using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <inheritdoc />
    public partial class AddPullRequestGithub : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PullRequestsGithub",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PullRequestRegisterId = table.Column<int>(type: "int", nullable: false),
                    CardNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RepositoryId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BranchPrefix = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BranchName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetBranch = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GithubPrNumber = table.Column<int>(type: "int", nullable: true),
                    GithubPrId = table.Column<long>(type: "bigint", nullable: true),
                    Url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDraft = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StatusSyncedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedBy = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestsGithub", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequestsGithub_PullRequests_PullRequestRegisterId",
                        column: x => x.PullRequestRegisterId,
                        principalTable: "PullRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestsGithub_CardNumber",
                table: "PullRequestsGithub",
                column: "CardNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestsGithub_PullRequestRegisterId",
                table: "PullRequestsGithub",
                column: "PullRequestRegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestsGithub_RepositoryId_GithubPrNumber",
                table: "PullRequestsGithub",
                columns: new[] { "RepositoryId", "GithubPrNumber" },
                unique: true);

            // Mantém os dados antigos: cada linha do modelo anterior (card x repositório, salva em
            // PullRequestsLegacyBackup) vira um registro LEGACY — branch/repositório/descrição/autor/
            // datas preservados; sem PR no GitHub (número/id nulos). Padrões para o que faltava:
            // repositório 'edv-solvace' e branch 'hotfix/<card>' (os mesmos defaults da tela antiga),
            // destino vazio e título 'AB#<card>'.
            migrationBuilder.Sql(@"
INSERT INTO PullRequestsGithub
    (PullRequestRegisterId, CardNumber, RepositoryId, BranchPrefix, BranchName, TargetBranch,
     GithubPrNumber, GithubPrId, Url, Title, Description, Status, IsDraft, StatusSyncedAt,
     UserId, CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
SELECT p.Id, p.CardNumber,
       LEFT(COALESCE(NULLIF(TRIM(b.RepositoryId), ''), 'edv-solvace'), 200),
       LEFT(COALESCE(NULLIF(TRIM(b.BranchPrefix), ''), 'hotfix/'), 50),
       LEFT(COALESCE(NULLIF(TRIM(b.BranchName), ''), p.CardNumber), 200),
       '', NULL, NULL, '',
       LEFT(CONCAT('AB#', p.CardNumber), 500),
       b.Description, 'LEGACY', 0, NULL,
       b.UserId, b.CreatedAt, b.UpdatedAt, b.CreatedBy, b.UpdatedBy
FROM PullRequestsLegacyBackup b
JOIN PullRequests p ON p.CardNumber = TRIM(b.CardNumber)
ORDER BY b.Id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PullRequestsGithub");
        }
    }
}
