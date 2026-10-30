using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0074: perfis de destino da promoção de PRs em lote (release/hotfix) por tipo de repositório e o padrão do nome da
    /// branch do cherry-pick, no "Skills Configurations". Só as chaves que faltam — o que o admin já editou é preservado.
    /// O <c>kind</c> de cada perfil é o do <c>BranchStrategy.repositories</c>; <c>{version}</c> vira a versão do lote.
    /// </summary>
    public partial class SeedPromotionProfiles : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string Profiles = """
{
  "profiles": {
    "legacy": {
      "label": "Netcore (legado)",
      "targets": [ { "branch": "hotfix-{version}" } ]
    },
    "integration-api": {
      "label": "API de Integrações",
      "targets": [ { "branch": "release-{version}" }, { "branch": "master" } ]
    },
    "revamp-frontend": {
      "label": "Angular (revamp)",
      "targets": [ { "branch": "release-{version}" }, { "branch": "edge" }, { "branch": "master", "draft": true } ]
    },
    "revamp-backend": {
      "label": "Revamp (back)",
      "targets": [ { "branch": "release-{version}" }, { "branch": "edge" }, { "branch": "master", "draft": true } ]
    }
  }
}
""";

        private const string BranchPattern = "{branch}-{target}";

        private const string FieldSettings = """
{"PromotionProfiles": {"Label": "Promoção em lote: destinos por tipo de repositório (JSON: profiles → kind → targets[{branch, draft?, mode?}]; {version} = versão do lote)"}, "PromotionBranchPattern": {"Label": "Promoção em lote: nome da branch do cherry-pick ({branch} = branch principal do card, {target} = destino)"}}
""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(
                        COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb), '{0}',
                        jsonb_build_object('PromotionProfiles', $prof${{Profiles}}$prof$::text, 'PromotionBranchPattern', '{{BranchPattern}}')
                            || COALESCE(COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb) -> 0, '{}'::jsonb),
                        true)::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted";

                UPDATE prform."Plugins"
                SET "FieldSettings" = ('{{FieldSettings}}'::jsonb || COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb))::text,
                    "UpdatedAt" = now()
                WHERE "Description" = '{{SkillsPlugin}}' AND NOT "IsDeleted";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - ARRAY['PromotionProfiles', 'PromotionBranchPattern'])::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - ARRAY['PromotionProfiles', 'PromotionBranchPattern'])::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
