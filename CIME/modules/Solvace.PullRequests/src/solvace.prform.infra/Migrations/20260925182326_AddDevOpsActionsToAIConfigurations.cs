using Microsoft.EntityFrameworkCore.Migrations;
using Newtonsoft.Json;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// Feature 0011 ("Ações DevOps"): acrescenta ao plugin "AI Configurations" o prompt do resumo
    /// não técnico (fixo, do admin) e os valores das ações de Bug (pessoais e opcionais — área/estado/
    /// comentário com padrão global; estimativa inicial só vale depois que o usuário salva). O plugin
    /// passa a ser pessoal e opcional: como todos os campos do usuário são opcionais, ninguém fica
    /// "pendente" e as chamadas de IA continuam funcionando sem configuração.
    /// Idempotente: <c>JSON_INSERT</c> não sobrescreve chaves que já existem; sem o plugin, não faz nada.
    /// </summary>
    public partial class AddDevOpsActionsToAIConfigurations : Migration
    {
        private const string PluginName = "AI Configurations";

        private const string SummaryPrompt =
            "Você é um redator que explica correções de software para usuários de negócio.\n\n" +
            "Escreva um resumo NÃO técnico do card {cardNumber} ({title}), com um panorama do problema e da solução adotada, " +
            "em linguagem de negócio/usual: sem jargão técnico, sem nomes de arquivos, métodos, classes, tabelas ou código. " +
            "Foque no impacto para o usuário e no que passou a funcionar.\n\n" +
            "Problema reportado:\n{reproSteps}\n\n" +
            "Descrição técnica do Pull Request:\n{description}\n\n" +
            "Root cause (técnico):\n{rootCause}\n\n" +
            "Responda SOMENTE com o texto final em Markdown, exatamente neste formato (dois blocos com o mesmo conteúdo):\n\n" +
            "**PT**\n\n---\n\n<texto em português do Brasil>\n\n**EN**\n\n---\n\n<same text in en-US>\n\n" +
            "Regras: parágrafos curtos; explique o que acontecia de errado e o que muda para o usuário agora; " +
            "não mencione que o texto foi gerado por IA.";

        // Chave → valor global (padrão ou sugestão).
        private static readonly (string Key, string Value)[] NewKeys =
        {
            ("BugSummaryPrompt", SummaryPrompt),
            ("BugTestInProductionRequiredArea", @"Solvace Product Improvement\Product Development Team"),
            ("BugTestInProductionArea", @"Solvace Product Improvement\Release Management"),
            ("BugTestInProductionState", "Test in production"),
            ("BugTestInProductionComment", "moving to test in production"),
            ("BugReadyForQaState", "In Development (done)"),
            ("BugInitialOriginalEstimate", "6"),
            ("BugInitialRemainingWork", "6"),
            ("BugInitialCompletedWork", "0"),
        };

        private static readonly string[] PersonalKeys =
        {
            "BugTestInProductionArea", "BugTestInProductionState", "BugTestInProductionComment", "BugReadyForQaState",
            "BugInitialOriginalEstimate", "BugInitialRemainingWork", "BugInitialCompletedWork",
        };

        private static readonly Dictionary<string, object> FieldSettings = new()
        {
            ["Provider"] = new { label = "Provedor de IA", hidden = true },
            ["PromptBug"] = new { label = "Prompt do PR (Bug)", hidden = true },
            ["PromptUS"] = new { label = "Prompt do PR (User Story)", hidden = true },
            ["BugSummaryPrompt"] = new { label = "Prompt do resumo não técnico (Bug)", hidden = true },
            ["BugTestInProductionRequiredArea"] = new { label = "Área exigida para mover para Test in production (Bug)" },
            ["BugTestInProductionArea"] = new { label = "Área ao mover para Test in production (Bug)", optional = true, useGlobalDefault = true },
            ["BugTestInProductionState"] = new { label = "Estado ao mover para Test in production (Bug)", optional = true, useGlobalDefault = true },
            ["BugTestInProductionComment"] = new { label = "Comentário ao mover para Test in production (Bug)", optional = true, useGlobalDefault = true },
            ["BugReadyForQaState"] = new { label = "Estado ao mover para Ready for QA (Bug)", optional = true, useGlobalDefault = true },
            ["BugInitialOriginalEstimate"] = new { label = "Estimativa inicial — Original Estimate (Bug)", optional = true },
            ["BugInitialRemainingWork"] = new { label = "Estimativa inicial — Remaining Work (Bug)", optional = true },
            ["BugInitialCompletedWork"] = new { label = "Estimativa inicial — Completed Work (Bug)", optional = true },
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var inserts = string.Join(", ", NewKeys.Select(k => $"'$[0].{k.Key}', {Literal(k.Value)}"));

            // Chaves novas no JSON existente ([{...}]); JSON_INSERT mantém valores já configurados.
            migrationBuilder.Sql($@"
UPDATE `PluginConfigurations` c
JOIN `Plugins` p ON p.`Id` = c.`PluginId`
SET c.`Options` = JSON_INSERT(c.`Options`, {inserts})
WHERE p.`Description` = {Literal(PluginName)} AND p.`IsDeleted` = 0
  AND c.`Options` IS NOT NULL AND JSON_VALID(c.`Options`) AND JSON_TYPE(JSON_EXTRACT(c.`Options`, '$[0]')) = 'OBJECT';");

            var personal = Literal(JsonConvert.SerializeObject(PersonalKeys));
            var settings = Literal(JsonConvert.SerializeObject(FieldSettings));

            // Pessoal + opcional. Já pessoal com lista explícita: soma as chaves novas à lista.
            // FieldSettings existente prevalece sobre o desta migração.
            migrationBuilder.Sql($@"
UPDATE `Plugins`
SET `PersonalFields` = CASE
        WHEN `IsPersonal` = 1 AND `PersonalFields` IS NULL THEN NULL
        WHEN `IsPersonal` = 1 AND JSON_VALID(`PersonalFields`) THEN JSON_MERGE_PRESERVE(`PersonalFields`, {personal})
        ELSE {personal}
    END,
    `FieldSettings` = CASE
        WHEN `FieldSettings` IS NOT NULL AND JSON_VALID(`FieldSettings`) THEN JSON_MERGE_PATCH({settings}, `FieldSettings`)
        ELSE {settings}
    END,
    `IsPersonal` = 1,
    `IsOptional` = 1,
    `UpdatedAt` = UTC_TIMESTAMP(6),
    `UpdatedBy` = 'migration:AddDevOpsActionsToAIConfigurations'
WHERE `Description` = {Literal(PluginName)} AND `IsDeleted` = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var removes = string.Join(", ", NewKeys.Select(k => $"'$[0].{k.Key}'"));

            migrationBuilder.Sql($@"
UPDATE `PluginConfigurations` c
JOIN `Plugins` p ON p.`Id` = c.`PluginId`
SET c.`Options` = JSON_REMOVE(c.`Options`, {removes})
WHERE p.`Description` = {Literal(PluginName)} AND c.`Options` IS NOT NULL AND JSON_VALID(c.`Options`);");

            migrationBuilder.Sql($@"
UPDATE `Plugins`
SET `IsPersonal` = 0, `IsOptional` = 0, `PersonalFields` = NULL, `FieldSettings` = NULL
WHERE `Description` = {Literal(PluginName)};");
        }

        /// <summary>Literal de string MySQL: escapa barra invertida (área do DevOps, "\n" do JSON) e aspas.</summary>
        private static string Literal(string value) =>
            "'" + value.Replace("\\", "\\\\").Replace("'", "''") + "'";
    }
}
