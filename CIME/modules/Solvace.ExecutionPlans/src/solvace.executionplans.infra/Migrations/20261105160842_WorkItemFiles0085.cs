using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.executionplans.infra.Migrations
{
    /// <inheritdoc />
    public partial class WorkItemFiles0085 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Hidden",
                schema: "execution",
                table: "CaseFiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // 0085 r5 (1.7): áudios já enviados por pessoas (mensagens de voz) saem dos arquivos da conversa.
            migrationBuilder.Sql(
                """UPDATE execution."CaseFiles" SET "Hidden" = true WHERE "Kind" = 'audio' AND NOT "FromSpecialist";""");

            migrationBuilder.CreateTable(
                name: "WorkItemFiles",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskNumber = table.Column<long>(type: "bigint", nullable: true),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemFiles_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalSchema: "execution",
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemFileContents",
                schema: "execution",
                columns: table => new
                {
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemFileContents", x => x.FileId);
                    table.ForeignKey(
                        name: "FK_WorkItemFileContents_WorkItemFiles_FileId",
                        column: x => x.FileId,
                        principalSchema: "execution",
                        principalTable: "WorkItemFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemFiles_WorkItemId_Kind_Name",
                schema: "execution",
                table: "WorkItemFiles",
                columns: new[] { "WorkItemId", "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemFiles_WorkItemId_Number",
                schema: "execution",
                table: "WorkItemFiles",
                columns: new[] { "WorkItemId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkItemFileContents",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "WorkItemFiles",
                schema: "execution");

            migrationBuilder.DropColumn(
                name: "Hidden",
                schema: "execution",
                table: "CaseFiles");
        }
    }
}
