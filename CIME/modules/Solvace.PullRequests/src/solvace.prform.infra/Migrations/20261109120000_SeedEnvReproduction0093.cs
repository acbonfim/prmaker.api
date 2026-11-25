using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0093: reproduzir o bug no ambiente — idempotente e só acrescenta no "Skills Configurations": tipo de sub-tarefa
    /// <c>env-reproduction</c>, tipo de item <c>reproduction</c> e o modelo <c>reproduzir-bug-no-ambiente</c> (por padrão o
    /// ambiente relatado pelo cliente; outro ambiente é opcional). Usa os ambientes de <c>ValidationEnvironments</c> (0092).
    /// JSON inválido do admin: a linha fica como está. Down tira só o que ainda está exatamente como esta migração pôs.
    /// </summary>
    public partial class SeedEnvReproduction0093 : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        public const string NewKind = """
{ "kind": "env-reproduction", "label": "Reproduzir no ambiente", "icon": "bug_report", "executor": "claude",
  "description": "Entra no ambiente (por padrão o relatado pelo cliente no card; a senha fica só na máquina de quem executa), segue os passos para reproduzir do card e grava a sequência de prints mostrando o erro (ou que não reproduziu) como evidência desta sub-tarefa. DEMO e produção: só navegar e consultar — o passo que grava dados vai para o sandbox do cliente.",
  "params": [
    { "key": "environment", "label": "Ambiente (cliente = o relatado no card)", "required": true },
    { "key": "plant", "label": "Planta (sigla)" },
    { "key": "user", "label": "Usuário (e-mail)" },
    { "key": "steps", "label": "Passos para reproduzir (vazio = os do card)", "input": "textarea" },
    { "key": "expected", "label": "Comportamento esperado", "input": "textarea" }
  ] }
""";

        public const string NewItemType = """
{ "key": "reproduction", "label": "Reprodução", "icon": "bug_report", "color": "#c62828", "description": "Reproduzir o bug num ambiente (por padrão o do cliente), com os prints como evidência." }
""";

        public const string NewTemplate = """
{
  "key": "reproduzir-bug-no-ambiente", "type": "reproduction", "group": "QA", "icon": "bug_report",
  "title": "Reproduzir o bug ({ask:environments})",
  "description": "O Claude entra no ambiente relatado pelo cliente (ou no que você indicar), segue os passos para reproduzir do card e grava a sequência de prints mostrando o erro — ou que não reproduziu — como evidência na sub-tarefa do ambiente. DEMO e produção: só navegar e consultar (o passo que grava dados vai para o sandbox do cliente).",
  "when": { "type": ["bug"] },
  "ask": [
    { "key": "environments", "label": "Ambiente (padrão: o relatado pelo cliente)", "input": "environments", "options": "{reproductionEnvironments}", "default": "cliente", "required": true },
    { "key": "plant", "label": "Planta (sigla, opcional — padrão: a do card)", "input": "text", "required": false },
    { "key": "user", "label": "Usuário (opcional — padrão: admin do ambiente ou da planta)", "input": "text", "required": false },
    { "key": "steps", "label": "Passos para reproduzir (opcional — padrão: os do card)", "input": "textarea", "required": false }
  ],
  "tasks": [
    { "title": "Reproduzir o bug — {eachLabel}", "kind": "env-reproduction", "repeat": "{ask:environments}",
      "params": { "environment": "{each}", "plant": "{ask:plant}", "user": "{ask:user}", "steps": "{ask:steps}", "expected": "" } }
  ]
}
""";

        private static string Swap(bool up) => $$"""
            DO $mig$
            DECLARE
                r record; opts jsonb; kinds jsonb; tpls jsonb; types jsonb; changed boolean;
                nk jsonb := $nk${{NewKind}}$nk$::jsonb;
                nt jsonb := $nt${{NewTemplate}}$nt$::jsonb;
                ni jsonb := $ni${{NewItemType}}$ni$::jsonb;
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
                    EXCEPTION WHEN others THEN
                        CONTINUE;
                    END;
                    changed := false;

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
