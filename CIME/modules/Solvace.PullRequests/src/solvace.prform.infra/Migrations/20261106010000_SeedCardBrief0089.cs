using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0089: ferramenta "Resumo do pedido" do Especialista — idempotente e só acrescenta no "Skills Configurations":
    /// <list type="bullet">
    /// <item>tipo de sub-tarefa <c>card-brief</c> (executor Claude: lê o card inteiro e escreve o racional do pedido) se faltar;</item>
    /// <item>modelo <c>resumo-do-pedido</c> (Bug e User Story; publicar na Timeline é opcional) se a key faltar.</item>
    /// </list>
    /// JSON inválido do admin: a linha fica como está. Down tira só o que ainda está exatamente como esta migração pôs.
    /// </summary>
    public partial class SeedCardBrief0089 : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        public const string NewKind = """
{ "kind": "card-brief", "label": "Resumo do pedido", "icon": "summarize", "executor": "claude",
  "description": "Racional, em português, do que o card pede (User Story) ou reclama (Bug): lê título, campos, descrição/repro steps, critérios de aceite, discussion e as evidências (anexos e prints). Só leitura; publicar na Timeline é opcional.",
  "params": [
    { "key": "focus", "label": "O que destacar", "input": "textarea" },
    { "key": "timeline", "label": "Publicar na Timeline", "input": "select", "options": ["não", "sim"] }
  ] }
""";

        public const string NewTemplate = """
{
  "key": "resumo-do-pedido", "type": "text", "group": "Textos",
  "title": "Resumo do pedido (racional do card)",
  "description": "Lê o card inteiro — campos, descrição/repro steps, critérios de aceite, discussion, anexos e prints — e escreve, em português (mesmo com o card em inglês), o racional do que está sendo pedido (User Story) ou reclamado (Bug): quem, o quê, onde, por quê, como saber que foi atendido e o que ainda está em aberto.",
  "when": { "type": ["bug", "user-story"] },
  "ask": [
    { "key": "focus", "label": "O que destacar (opcional)", "input": "textarea" },
    { "key": "timeline", "label": "Publicar na Timeline?", "input": "select", "options": ["não", "sim"], "default": "não", "required": true }
  ],
  "tasks": [
    { "title": "Ler o card e escrever o racional do pedido", "kind": "card-brief",
      "params": { "focus": "{ask:focus}", "timeline": "{ask:timeline}" } }
  ]
}
""";

        private static string Swap(bool up) => $$"""
            DO $mig$
            DECLARE
                r record; opts jsonb; kinds jsonb; tpls jsonb; changed boolean;
                nk jsonb := $nk${{NewKind}}$nk$::jsonb;
                nt jsonb := $nt${{NewTemplate}}$nt$::jsonb;
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
                    EXCEPTION WHEN others THEN
                        CONTINUE;
                    END;
                    changed := false;

                    IF kinds IS NOT NULL AND jsonb_typeof(kinds) = 'array' THEN
                        IF {{(up ? "true" : "false")}} AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(kinds) x WHERE x ->> 'kind' = nk ->> 'kind') THEN
                            kinds := kinds || jsonb_build_array(nk); changed := true;
                        ELSIF {{(up ? "false" : "true")}} AND EXISTS (SELECT 1 FROM jsonb_array_elements(kinds) x WHERE x = nk) THEN
                            SELECT COALESCE(jsonb_agg(e ORDER BY o), '[]'::jsonb) INTO kinds
                            FROM jsonb_array_elements(kinds) WITH ORDINALITY AS t(e, o) WHERE t.e <> nk;
                            changed := true;
                        END IF;
                        IF changed THEN
                            opts := jsonb_set(opts, '{0,WorkItemTaskKinds}', to_jsonb(jsonb_pretty(kinds)), true);
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
