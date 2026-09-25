using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// Feature 0011: configuração por campo dos plugins (<c>Plugins.FieldSettings</c> — nome amigável,
    /// campo opcional, padrão global, oculto) e <c>PluginConfigurations.Options</c> de varchar(4000)
    /// para longtext (os prompts do "AI Configurations" não cabiam).
    /// </summary>
    public partial class AddPluginFieldSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FieldSettings",
                table: "Plugins",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Options",
                table: "PluginConfigurations",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FieldSettings",
                table: "Plugins");

            migrationBuilder.AlterColumn<string>(
                name: "Options",
                table: "PluginConfigurations",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
