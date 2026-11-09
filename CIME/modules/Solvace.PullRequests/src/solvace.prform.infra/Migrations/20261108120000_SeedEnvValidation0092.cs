using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0092: validação em ambiente — idempotente e só acrescenta no "Skills Configurations":
    /// <list type="bullet">
    /// <item><c>ValidationEnvironments</c> (ambientes conhecidos, padrão do admin por planta, banco corporativo) se a chave faltar;</item>
    /// <item>tipo de sub-tarefa <c>env-validation</c> e tipo de item <c>validation</c> se faltarem;</item>
    /// <item>modelo <c>validar-em-ambiente</c> (uma sub-tarefa por ambiente escolhido) se a key faltar.</item>
    /// </list>
    /// JSON inválido do admin: a linha fica como está. Down tira só o que ainda está exatamente como esta migração pôs.
    /// </summary>
    public partial class SeedEnvValidation0092 : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        /// <summary>O mesmo de <c>solvace.prform.Skills.ValidationEnvironments.DefaultJson</c> (API).</summary>
        public const string Environments = """
{
  "environments": [
    { "key": "dev", "label": "Desenvolvimento", "url": "https://devsp.solvacelabs.com/", "user": "admin@ewcm.com.br", "kind": "dev",
      "database": { "host": "dev", "global": "DB_SOLV_DEV_GLOBAL" } },
    { "key": "qa", "label": "QA", "url": "https://qa.solvacelabs.com/", "user": "admin@ewcm.com.br", "kind": "qa",
      "database": { "host": "qa", "global": "DB_SOLV_QA_GLOBAL" } },
    { "key": "performancetest", "label": "Teste e performance", "url": "https://performancetest.solvacelabs.com/", "kind": "test" },
    { "key": "sandboxefeso", "label": "Homologação de release", "url": "https://sandboxefeso.solvacelabs.com/", "kind": "homolog",
      "database": { "host": "sandboxefeso", "global": "DB_EFESO_TST_GLOBAL" } },
    { "key": "sandboxrt", "label": "Homologação de release/hotfix", "url": "https://sandboxrt.solvacelabs.com/", "kind": "homolog" },
    { "key": "demo", "label": "Demonstração", "url": "https://demo.solvacelabs.com/", "kind": "demo", "readOnly": true,
      "note": "Jamais alterar dados na DEMO: só navegar e consultar.",
      "database": { "host": "prod", "global": "DB_DEMO_PRD_GLOBAL" } },
    { "key": "gerdau", "label": "Produção Gerdau", "url": "https://gerdau.solvacelabs.com/", "kind": "production", "client": "Gerdau", "readOnly": true,
      "database": { "host": "prod1", "global": "DB_GERDAU_PRD_GLOBAL" } },
    { "key": "mondelez", "label": "Produção Mondelez", "url": "https://mondelez.solvacelabs.com/", "kind": "production", "client": "Mondelez", "readOnly": true,
      "database": { "host": "prod", "global": "DB_MDLZ_PRD_GLOBAL" } }
  ],
  "plantAdminPattern": "admin{plant}@solvacelabs.com",
  "corporate": { "host": "corporate", "database": "CORPORATE_SOLVACE_DB", "table": "TB_WCM_ENVIRONMENT" },
  "productionReadOnly": true,
  "defaults": { "bug": ["dev", "qa"], "user-story": ["dev", "qa"] }
}
""";

        public const string NewKind = """
{ "kind": "env-validation", "label": "Validar no ambiente", "icon": "screenshot_monitor", "executor": "claude",
  "description": "Entra no ambiente (URL e usuário da configuração ValidationEnvironments; a senha fica só na máquina de quem executa), segue o roteiro e grava a sequência de prints como evidência desta sub-tarefa. DEMO e produção: só navegar e consultar.",
  "params": [
    { "key": "environment", "label": "Ambiente", "required": true },
    { "key": "plant", "label": "Planta (sigla)" },
    { "key": "user", "label": "Usuário (e-mail)" },
    { "key": "scenario", "label": "O que validar", "input": "textarea" },
    { "key": "expected", "label": "Resultado esperado", "input": "textarea" }
  ] }
""";

        public const string NewItemType = """
{ "key": "validation", "label": "Validação", "icon": "verified", "color": "#2e7d32", "description": "Validar a demanda em ambientes (uma sub-tarefa por ambiente, com os prints como evidência)." }
""";

        public const string NewTemplate = """
{
  "key": "validar-em-ambiente", "type": "validation", "group": "QA",
  "title": "Validar em ambiente ({ask:environments})",
  "description": "O Claude entra em cada ambiente escolhido (navegador da máquina de quem executa, com a senha guardada só nela), segue o roteiro e grava a sequência de prints mostrando o funcionamento como evidência na sub-tarefa do ambiente. DEMO e produção: só navegar e consultar.",
  "when": { "type": ["bug", "user-story"] },
  "ask": [
    { "key": "environments", "label": "Ambientes", "input": "environments", "options": "{validationEnvironments}", "default": "{validationDefaults}", "required": true },
    { "key": "plant", "label": "Planta (sigla, opcional)", "input": "text", "required": false },
    { "key": "user", "label": "Usuário (opcional — padrão: admin do ambiente ou da planta)", "input": "text", "required": false },
    { "key": "scenario", "label": "O que validar (opcional — padrão: o que o card pede)", "input": "textarea", "required": false }
  ],
  "tasks": [
    { "title": "Validar em {eachLabel}", "kind": "env-validation", "repeat": "{ask:environments}",
      "params": { "environment": "{each}", "plant": "{ask:plant}", "user": "{ask:user}", "scenario": "{ask:scenario}", "expected": "" } }
  ]
}
""";

        private static string Swap(bool up) => $$"""
            DO $mig$
            DECLARE
                r record; opts jsonb; kinds jsonb; tpls jsonb; types jsonb; envs text; changed boolean;
                nk jsonb := $nk${{NewKind}}$nk$::jsonb;
                nt jsonb := $nt${{NewTemplate}}$nt$::jsonb;
                ni jsonb := $ni${{NewItemType}}$ni$::jsonb;
                ne jsonb := $ne${{Environments}}$ne$::jsonb;
            BEGIN
                FOR r IN
                    SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                    JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                    WHERE p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted" AND NULLIF(c."Options", '') IS NOT NULL
                LOOP
                    BEGIN
                        opts := r."Options"::jsonb;
                        IF jsonb_typeof(opts) <> 'array' OR jsonb_typeof(opts -> 0) <> 'object' THEN CONTINUE; END IF;
                        kinds := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTaskKinds', '')), '')::jsonb;
                        tpls := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTemplates', '')), '')::jsonb;
                        types := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTypes', '')), '')::jsonb;
                        envs := opts -> 0 ->> 'ValidationEnvironments';
                    EXCEPTION WHEN others THEN
                        CONTINUE;
                    END;
                    changed := false;

                    IF {{(up ? "true" : "false")}} AND (envs IS NULL OR btrim(envs) = '') THEN
                        opts := jsonb_set(opts, '{0,ValidationEnvironments}', to_jsonb(jsonb_pretty(ne)), true); changed := true;
                    ELSIF {{(up ? "false" : "true")}} AND envs IS NOT NULL THEN
                        BEGIN
                            IF envs::jsonb = ne THEN
                                opts := jsonb_set(opts, '{0}', (opts -> 0) - 'ValidationEnvironments'); changed := true;
                            END IF;
                        EXCEPTION WHEN others THEN
                            NULL;
                        END;
                    END IF;

                    IF kinds IS NOT NULL AND jsonb_typeof(kinds) = 'array' THEN
                        IF {{(up ? "true" : "false")}} AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(kinds) x WHERE x ->> 'kind' = nk ->> 'kind') THEN
                            kinds := kinds || jsonb_build_array(nk);
                            opts := jsonb_set(opts, '{0,WorkItemTaskKinds}', to_jsonb(jsonb_pretty(kinds)), true); changed := true;
                        ELSIF {{(up ? "false" : "true")}} AND EXISTS (SELECT 1 FROM jsonb_array_elements(kinds) x WHERE x = nk) THEN
                            SELECT COALESCE(jsonb_agg(e ORDER BY o), '[]'::jsonb) INTO kinds
                            FROM jsonb_array_elements(kinds) WITH ORDINALITY AS t(e, o) WHERE t.e <> nk;
                            opts := jsonb_set(opts, '{0,WorkItemTaskKinds}', to_jsonb(jsonb_pretty(kinds)), true); changed := true;
                        END IF;
                    END IF;

                    IF types IS NOT NULL AND jsonb_typeof(types) = 'array' THEN
                        IF {{(up ? "true" : "false")}} AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(types) x WHERE x ->> 'key' = ni ->> 'key') THEN
                            types := types || jsonb_build_array(ni);
                            opts := jsonb_set(opts, '{0,WorkItemTypes}', to_jsonb(jsonb_pretty(types)), true); changed := true;
                        ELSIF {{(up ? "false" : "true")}} AND EXISTS (SELECT 1 FROM jsonb_array_elements(types) x WHERE x = ni) THEN
                            SELECT COALESCE(jsonb_agg(e ORDER BY o), '[]'::jsonb) INTO types
                            FROM jsonb_array_elements(types) WITH ORDINALITY AS t(e, o) WHERE t.e <> ni;
                            opts := jsonb_set(opts, '{0,WorkItemTypes}', to_jsonb(jsonb_pretty(types)), true); changed := true;
                        END IF;
                    END IF;

                    IF tpls IS NOT NULL AND jsonb_typeof(tpls) = 'array' THEN
                        IF {{(up ? "true" : "false")}} AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(tpls) x WHERE x ->> 'key' = nt ->> 'key') THEN
                            tpls := tpls || jsonb_build_array(nt);
                            opts := jsonb_set(opts, '{0,WorkItemTemplates}', to_jsonb(jsonb_pretty(tpls)), true); changed := true;
                        ELSIF {{(up ? "false" : "true")}} AND EXISTS (SELECT 1 FROM jsonb_array_elements(tpls) x WHERE x = nt) THEN
                            SELECT COALESCE(jsonb_agg(e ORDER BY o), '[]'::jsonb) INTO tpls
                            FROM jsonb_array_elements(tpls) WITH ORDINALITY AS t(e, o) WHERE t.e <> nt;
                            opts := jsonb_set(opts, '{0,WorkItemTemplates}', to_jsonb(jsonb_pretty(tpls)), true); changed := true;
                        END IF;
                    END IF;

                    IF changed THEN
                        UPDATE prform."PluginConfigurations" SET "Options" = opts::text WHERE "Id" = r."Id";
                    END IF;
                END LOOP;
            END
            $mig$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Swap(true));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Swap(false));
    }
}
