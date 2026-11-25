using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <inheritdoc />
    public partial class PluginSettings0086 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0086: xmin é coluna de sistema do PostgreSQL — o Npgsql não gera SQL para ela (fica só no modelo, como token
            // de concorrência do PATCH das configurações). A migração só cria o histórico.
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "prform",
                table: "PluginConfigurations",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "PluginSettingChanges",
                schema: "prform",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PluginId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OldValue = table.Column<string>(type: "text", nullable: true),
                    NewValue = table.Column<string>(type: "text", nullable: true),
                    ChangedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PluginSettingChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PluginSettingChanges_ChangedAt",
                schema: "prform",
                table: "PluginSettingChanges",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PluginSettingChanges_PluginId_Key_ChangedAt",
                schema: "prform",
                table: "PluginSettingChanges",
                columns: new[] { "PluginId", "Key", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PluginSettingChanges",
                schema: "prform");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "prform",
                table: "PluginConfigurations");
        }
    }
}
