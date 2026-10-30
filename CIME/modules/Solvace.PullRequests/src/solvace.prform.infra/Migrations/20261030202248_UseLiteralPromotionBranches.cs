using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0074: <c>release-version</c> e <c>hotfix-version</c> são nomes REAIS de branch (é como o <c>BranchStrategy</c> já os
    /// chama em <c>baseOptions</c>), e não modelos com número de versão. A <c>SeedPromotionProfiles</c> semeou
    /// <c>hotfix-{version}</c>/<c>release-{version}</c>, que não existem no remoto. Troca pelos nomes reais — só se o valor
    /// ainda for exatamente o semeado (o que o admin editou é preservado).
    /// </summary>
    public partial class UseLiteralPromotionBranches : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string Old = """
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

        private const string New = """
{
  "profiles": {
    "legacy": {
      "label": "Netcore (legado)",
      "targets": [ { "branch": "hotfix-version" } ]
    },
    "integration-api": {
      "label": "API de Integrações",
      "targets": [ { "branch": "release-version" }, { "branch": "master" } ]
    },
    "revamp-frontend": {
      "label": "Angular (revamp)",
      "targets": [ { "branch": "release-version" }, { "branch": "edge" }, { "branch": "master", "draft": true } ]
    },
    "revamp-backend": {
      "label": "Revamp (back)",
      "targets": [ { "branch": "release-version" }, { "branch": "edge" }, { "branch": "master", "draft": true } ]
    }
  }
}
""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => Swap(migrationBuilder, Old, New);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => Swap(migrationBuilder, New, Old);

        private static void Swap(MigrationBuilder migrationBuilder, string from, string to) =>
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0,PromotionProfiles}', to_jsonb($to${{to}}$to$::text), false)::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted"
                  AND NULLIF(c."Options", '') IS NOT NULL
                  AND (c."Options"::jsonb -> 0 ->> 'PromotionProfiles') = $from${{from}}$from$;
                """);
    }
}
