using Microsoft.EntityFrameworkCore.Migrations;
using Newtonsoft.Json;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// Feature 0007: plugin pessoal opcional (<c>Plugins.IsOptional</c>) e o plugin
    /// "Teams Configurations" (pedido de aprovação de PR via Workflow do Teams). O usuário
    /// preenche só a URL do Workflow; o restante (nome do grupo e modelo da mensagem) é do admin.
    /// Insert idempotente: não duplica se o plugin já existir.
    /// </summary>
    public partial class AddTeamsPlugin : Migration
    {
        private const string PluginName = "Teams Configurations";
        private const string CreatedBy = "migration:AddTeamsPlugin";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOptional",
                table: "Plugins",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // Mesmo formato do PluginConfiguration: lista com um dicionário de chave → valor.
            var options = JsonConvert.SerializeObject(new[]
            {
                new Dictionary<string, string>
                {
                    ["WebhookUrl"] = "",
                    ["GroupName"] = "Aprovações de PR",
                    ["MessageTitle"] = "Aprovação de PR — AB#{cardNumber}",
                    ["MessageBody"] = "**{author}** pede aprovação do PR **#{prNumber}**\n\n{repository}: {branch} → {targetBranch}\n\n{prTitle}",
                    ["ButtonText"] = "Abrir PR no GitHub",
                    ["TitleColor"] = "Accent",
                    ["IncludeDescription"] = "false",
                    ["DescriptionMaxLength"] = "1200",
                }
            });
            var personalFields = JsonConvert.SerializeObject(new[] { "WebhookUrl" });

            migrationBuilder.Sql($@"
INSERT INTO `Plugins` (`Description`, `CreatedAt`, `CreatedBy`, `AdminOnly`, `IsDeleted`, `IsPersonal`, `IsOptional`, `PersonalFields`)
SELECT {Literal(PluginName)}, UTC_TIMESTAMP(6), {Literal(CreatedBy)}, 0, 0, 1, 1, {Literal(personalFields)}
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM `Plugins` WHERE `Description` = {Literal(PluginName)} AND `IsDeleted` = 0);");

            migrationBuilder.Sql($@"
INSERT INTO `PluginConfigurations` (`PluginId`, `Options`)
SELECT p.`Id`, {Literal(options)}
FROM `Plugins` p
WHERE p.`Description` = {Literal(PluginName)} AND p.`IsDeleted` = 0
  AND NOT EXISTS (SELECT 1 FROM `PluginConfigurations` c WHERE c.`PluginId` = p.`Id`)
ORDER BY p.`Id`
LIMIT 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove só o que esta migração criou (plugin marcado com CreatedBy da migração).
            migrationBuilder.Sql($@"
DELETE c FROM `PluginConfigurations` c
JOIN `Plugins` p ON p.`Id` = c.`PluginId`
WHERE p.`Description` = {Literal(PluginName)} AND p.`CreatedBy` = {Literal(CreatedBy)};");

            migrationBuilder.Sql($@"
DELETE FROM `Plugins` WHERE `Description` = {Literal(PluginName)} AND `CreatedBy` = {Literal(CreatedBy)};");

            migrationBuilder.DropColumn(
                name: "IsOptional",
                table: "Plugins");
        }

        /// <summary>Literal de string MySQL: escapa barra invertida (o JSON tem "\n") e aspas.</summary>
        private static string Literal(string value) =>
            "'" + value.Replace("\\", "\\\\").Replace("'", "''") + "'";
    }
}
