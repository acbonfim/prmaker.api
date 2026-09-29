using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// Feature 0030: configurações que as skills e as Ações DevOps usam passam a ficar nos plugins (editáveis na
    /// tela, sem deploy). Cria o "Skills Configurations" e acrescenta chaves ao "AI Configurations" e ao
    /// "AzureDevOps Configurations" — só as que faltam: valores já editados pelo admin são preservados.
    /// </summary>
    public partial class SeedSkillsConfigurations : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";
        private const string AIPlugin = "AI Configurations";
        private const string AzurePlugin = "AzureDevOps Configurations";

        private const string SkillsOptions = """
{"BranchFlowByArea": "[\n  {\n    \"areaContains\": \"Release Management\",\n    \"flow\": \"release\"\n  },\n  {\n    \"areaContains\": \"Product Development Team\",\n    \"flow\": \"producao\"\n  }\n]", "BranchStrategy": "{\n  \"repositories\": [\n    {\n      \"match\": \"edv-solvace-apps\",\n      \"kind\": \"revamp-frontend\"\n    },\n    {\n      \"match\": \"edv-solvace\",\n      \"kind\": \"legacy\"\n    },\n    {\n      \"match\": \"revamp-*\",\n      \"kind\": \"revamp-backend\"\n    }\n  ],\n  \"flows\": {\n    \"producao\": {\n      \"legacy\": {\n        \"base\": \"master\",\n        \"prs\": [\n          {\n            \"suffix\": \"-dev\",\n            \"from\": \"development\",\n            \"target\": \"development\"\n          },\n          {\n            \"suffix\": \"-qa\",\n            \"from\": \"qa\",\n            \"target\": \"qa\"\n          }\n        ]\n      },\n      \"revamp-backend\": {\n        \"base\": \"master\",\n        \"prs\": [\n          {\n            \"suffix\": \"-dev\",\n            \"from\": \"development\",\n            \"target\": \"development\"\n          }\n        ]\n      },\n      \"revamp-frontend\": {\n        \"base\": \"master\",\n        \"prs\": [\n          {\n            \"suffix\": \"-dev\",\n            \"from\": \"development\",\n            \"target\": \"development\"\n          },\n          {\n            \"suffix\": \"-qa\",\n            \"from\": \"qa\",\n            \"target\": \"qa\"\n          }\n        ]\n      }\n    },\n    \"release\": {\n      \"legacy\": {\n        \"baseOptions\": [\n          \"release-version\",\n          \"hotfix-version\"\n        ],\n        \"askBase\": true,\n        \"prs\": [\n          {\n            \"suffix\": \"\",\n            \"target\": \"{base}\"\n          }\n        ]\n      },\n      \"revamp-backend\": {\n        \"baseOptions\": [\n          \"release-version\",\n          \"hotfix-version\"\n        ],\n        \"askBase\": true,\n        \"prs\": [\n          {\n            \"suffix\": \"\",\n            \"target\": \"{base}\"\n          }\n        ]\n      },\n      \"revamp-frontend\": {\n        \"baseOptions\": [\n          \"edge\"\n        ],\n        \"askBase\": true,\n        \"prs\": [\n          {\n            \"suffix\": \"\",\n            \"target\": \"{base}\"\n          }\n        ]\n      }\n    }\n  }\n}", "BranchNamePattern": "hotfix/{card}", "CommitMessagePattern": "AB#{card} {summary}", "PrTitlePattern": "AB#{card} {TARGET}", "DefaultRepository": "edv-solvace", "TicketSystem": "Freshservice"}
""";

        private const string SkillsFieldSettings = """
{"BranchFlowByArea": {"Label": "Fluxo de branches pela área do card (JSON: areaContains → flow; sem match = perguntar)"}, "BranchStrategy": {"Label": "Estratégia de branches por tipo de repositório e fluxo (JSON)"}, "BranchNamePattern": {"Label": "Nome da branch de correção ({card})"}, "CommitMessagePattern": {"Label": "Mensagem de commit ({card}, {summary})"}, "PrTitlePattern": {"Label": "Título do PR ({card}, {TARGET} = destino em maiúsculas, {target})"}, "DefaultRepository": {"Label": "Repositório padrão (sem git remote)"}, "TicketSystem": {"Label": "Sistema de chamados (nome exibido nas etapas)"}}
""";

        private const string AIOptions = """
{"BugClassificationPresets": "[\n  {\n    \"key\": \"code-fix\",\n    \"label\": \"Correção de código\",\n    \"resolutionType\": \"Code Fix\",\n    \"generalClassification\": \"Code\",\n    \"classification\": \"Code Required - Code Defect\",\n    \"pattern\": \"A\"\n  },\n  {\n    \"key\": \"code-data-fix\",\n    \"label\": \"Código + correção de dados causada pelo defeito\",\n    \"resolutionType\": \"Code Fix\",\n    \"generalClassification\": \"Code\",\n    \"classification\": \"Code Required - Data Fix / Request - Caused by Defect\",\n    \"pattern\": \"A+B\"\n  },\n  {\n    \"key\": \"script-defect\",\n    \"label\": \"Script de dados (causa: defeito)\",\n    \"resolutionType\": \"Configuration (Script)\",\n    \"generalClassification\": \"Code\",\n    \"classification\": \"Code Required - Data Fix / Request - Caused by Defect\",\n    \"pattern\": \"B\"\n  },\n  {\n    \"key\": \"script-user-action\",\n    \"label\": \"Script de dados (causa: ação do usuário)\",\n    \"resolutionType\": \"Configuration (Script)\",\n    \"generalClassification\": \"No Code\",\n    \"classification\": \"Code Required - Data Fix / Request - Caused by User Action\",\n    \"pattern\": \"B\"\n  },\n  {\n    \"key\": \"script-environment\",\n    \"label\": \"Script de ambiente/plataforma\",\n    \"resolutionType\": \"Configuration (Script)\",\n    \"generalClassification\": \"No Code\",\n    \"classification\": \"No Code Required - Environment / Platform\",\n    \"pattern\": \"B/C\"\n  },\n  {\n    \"key\": \"configuration\",\n    \"label\": \"Configuração de ambiente/plataforma\",\n    \"resolutionType\": \"Configuration\",\n    \"generalClassification\": \"No Code\",\n    \"classification\": \"No Code Required - Environment / Platform\",\n    \"pattern\": \"C/D\"\n  },\n  {\n    \"key\": \"configuration-change-request\",\n    \"label\": \"Configuração a pedido (change request)\",\n    \"resolutionType\": \"Configuration\",\n    \"generalClassification\": \"No Code\",\n    \"classification\": \"No Code Required - Not a Defect - Change Request / Missed Requirement\",\n    \"pattern\": \"D\"\n  },\n  {\n    \"key\": \"user-education\",\n    \"label\": \"Orientação ao cliente (não é defeito)\",\n    \"resolutionType\": \"User Education\",\n    \"generalClassification\": \"No Code\",\n    \"classification\": \"No Code Required - Not a Defect - Training\",\n    \"pattern\": \"E\"\n  },\n  {\n    \"key\": \"user-education-change-request\",\n    \"label\": \"Orientação + pedido de mudança\",\n    \"resolutionType\": \"User Education\",\n    \"generalClassification\": \"No Code\",\n    \"classification\": \"No Code Required - Not a Defect - Change Request / Missed Requirement\",\n    \"pattern\": \"E/F\"\n  },\n  {\n    \"key\": \"change-request\",\n    \"label\": \"Change request\",\n    \"resolutionType\": \"Change Request\",\n    \"generalClassification\": \"No Code\",\n    \"classification\": \"No Code Required - Not a Defect - Change Request / Missed Requirement\",\n    \"pattern\": \"F\"\n  },\n  {\n    \"key\": \"not-mapped-requirement\",\n    \"label\": \"Requisito não mapeado\",\n    \"resolutionType\": \"Not Mapped Requirement\",\n    \"generalClassification\": \"No Code\",\n    \"classification\": \"No Code Required - Not a Defect - Change Request / Missed Requirement\",\n    \"pattern\": \"F\"\n  },\n  {\n    \"key\": \"cannot-reproduce\",\n    \"label\": \"Não reproduz\",\n    \"resolutionType\": \"Cannot reproduce\",\n    \"generalClassification\": \"No Code\",\n    \"classification\": \"No Code Required - Environment / Platform\",\n    \"pattern\": \"G\"\n  },\n  {\n    \"key\": \"no-user-feedback\",\n    \"label\": \"Sem retorno do cliente\",\n    \"resolutionType\": \"Cannot reproduce\",\n    \"generalClassification\": \"No user feedback\",\n    \"classification\": \"No user feedback - Pending information\",\n    \"pattern\": \"G\"\n  },\n  {\n    \"key\": \"duplicated\",\n    \"label\": \"Duplicado\",\n    \"resolutionType\": \"Duplicated\",\n    \"generalClassification\": \"Duplicated\",\n    \"classification\": \"Ticket duplicated\",\n    \"pattern\": \"H\"\n  }\n]"}
""";

        private const string AIFieldSettings = """
{"BugClassificationPresets": {"Label": "Opções de classificação do card (JSON: key, label, resolutionType, generalClassification, classification, pattern)", "Hidden": true}}
""";

        private const string AzureOptions = """
{"FieldResolutionType": "Custom.ResolutionType", "FieldGeneralClassification": "Custom.GeneralClassification", "FieldClassification": "Custom.Classification", "FieldRemainingWork": "Microsoft.VSTS.Scheduling.RemainingWork", "FieldOriginalEstimate": "Microsoft.VSTS.Scheduling.OriginalEstimate", "FieldCompletedWork": "Microsoft.VSTS.Scheduling.CompletedWork"}
""";

        private const string AzureFieldSettings = """
{"FieldResolutionType": {"Label": "Campo: Resolution Type", "Hidden": true}, "FieldGeneralClassification": {"Label": "Campo: General Classification", "Hidden": true}, "FieldClassification": {"Label": "Campo: Classification", "Hidden": true}, "FieldRemainingWork": {"Label": "Campo: Remaining Work", "Hidden": true}, "FieldOriginalEstimate": {"Label": "Campo: Original Estimate", "Hidden": true}, "FieldCompletedWork": {"Label": "Campo: Completed Work", "Hidden": true}}
""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                INSERT INTO prform."Plugins" ("Description", "CreatedAt", "CreatedBy", "AdminOnly", "IsPersonal", "IsOptional", "IsDeleted")
                SELECT '{{SkillsPlugin}}', now(), 'migration:0030', false, false, false, false
                WHERE NOT EXISTS (SELECT 1 FROM prform."Plugins" WHERE "Description" = '{{SkillsPlugin}}' AND NOT "IsDeleted");

                INSERT INTO prform."PluginConfigurations" ("PluginId", "Options")
                SELECT p."Id", '[{}]'
                FROM prform."Plugins" p
                WHERE p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted"
                  AND NOT EXISTS (SELECT 1 FROM prform."PluginConfigurations" c WHERE c."PluginId" = p."Id");
                """);

            migrationBuilder.Sql(MergeKeys(SkillsPlugin, SkillsOptions, SkillsFieldSettings));
            migrationBuilder.Sql(MergeKeys(AIPlugin, AIOptions, AIFieldSettings));
            migrationBuilder.Sql(MergeKeys(AzurePlugin, AzureOptions, AzureFieldSettings));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RemoveKeys(AIPlugin, AIOptions));
            migrationBuilder.Sql(RemoveKeys(AzurePlugin, AzureOptions));
            migrationBuilder.Sql($$"""
                DELETE FROM prform."PluginConfigurations" c USING prform."Plugins" p
                WHERE c."PluginId" = p."Id" AND p."Description" = '{{SkillsPlugin}}';
                DELETE FROM prform."Plugins" WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }

        private static string Quote(string json) => json.Replace("'", "''");

        /// <summary>Acrescenta as chaves que faltam em Options[0] e em FieldSettings (as existentes vencem).</summary>
        private static string MergeKeys(string plugin, string options, string fieldSettings) => $$"""
            UPDATE prform."PluginConfigurations" c
            SET "Options" = jsonb_set(
                    COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb), '{0}',
                    '{{Quote(options)}}'::jsonb || COALESCE(COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb) -> 0, '{}'::jsonb),
                    true)::text
            FROM prform."Plugins" p
            WHERE p."Id" = c."PluginId" AND p."Description" = '{{plugin}}' AND NOT p."IsDeleted";

            UPDATE prform."Plugins"
            SET "FieldSettings" = ('{{Quote(fieldSettings)}}'::jsonb || COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb))::text,
                "UpdatedAt" = now()
            WHERE "Description" = '{{plugin}}' AND NOT "IsDeleted";
            """;

        private static string RemoveKeys(string plugin, string options) => $$"""
            UPDATE prform."PluginConfigurations" c
            SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - ARRAY(SELECT jsonb_object_keys('{{Quote(options)}}'::jsonb)))::text
            FROM prform."Plugins" p
            WHERE p."Id" = c."PluginId" AND p."Description" = '{{plugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

            UPDATE prform."Plugins"
            SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - ARRAY(SELECT jsonb_object_keys('{{Quote(options)}}'::jsonb)))::text
            WHERE "Description" = '{{plugin}}';
            """;
    }
}
