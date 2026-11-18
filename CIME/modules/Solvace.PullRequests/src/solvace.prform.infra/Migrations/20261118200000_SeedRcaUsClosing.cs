using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// Fechamento do RCA da User Story (card 78323) — idempotente e só acrescenta no "Skills Configurations" o modelo
    /// <c>fechamento-rca-us</c> (ferramenta do especialista): confere a validação em QA e os critérios de aceite, RCA no Modelo
    /// do prompt (a US não tem o campo de root cause: vai como comentário na discussion), classificação, resumo PT/EN,
    /// Remaining zerado e Ready for QA da US. Os modelos de fechamento do Bug (<c>fechamento-code-fix</c>,
    /// <c>fechamento-sem-codigo</c>) continuam só no Bug. JSON inválido do admin: a linha fica como está. Down tira só o que
    /// ainda está exatamente como esta migração pôs.
    /// </summary>
    public partial class SeedRcaUsClosing : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        public const string NewTemplate = """
{
  "key": "fechamento-rca-us", "type": "closing", "group": "Fechamento",
  "title": "Fechamento do RCA (User Story): validação em QA, RCA, classificação, resumo e Ready for QA",
  "description": "Para a US de RCA ([RCA] no título / Requirement Type RCA), depois do PR mesclado e da validação em QA: confere a validação e os critérios de aceite, publica o RCA no Modelo do prompt (a US não tem o campo de root cause: vai como comentário na discussion), classifica ({ask:classification}), publica o resumo PT/EN, zera o Remaining e move a US para o estado de pronto (Ready for QA da US).",
  "when": { "type": ["user-story"] },
  "ask": [
    { "key": "classification", "label": "Classificação", "input": "select", "default": "code-fix", "required": true,
      "options": ["code-fix", "code-data-fix", "script-defect", "script-user-action", "script-environment", "configuration", "configuration-change-request", "user-education", "user-education-change-request", "change-request", "not-mapped-requirement", "cannot-reproduce", "no-user-feedback", "duplicated"] },
    { "key": "gaps", "label": "Critérios não validados em QA que você aceita fechar assim (opcional — viram lacuna registrada)", "input": "textarea", "required": false }
  ],
  "tasks": [
    { "title": "Conferir a validação em QA e os critérios de aceite", "kind": "validation",
      "params": { "how": "Ler o item de validação em QA do card (resultado e prints) e os critérios de aceite: cada CA aprovado, reprovado ou não validado. Sem validação em QA aprovada (ou CA reprovado), a sub-tarefa espera a pessoa decidir. Lacunas aceitas pela pessoa: {ask:gaps}" } },
    { "title": "RCA no card (Modelo do RCA; na US vai na discussion)", "kind": "devops-action", "params": { "action": "rootcause", "value": "" } },
    { "title": "Classificar ({ask:classification})", "kind": "devops-action", "params": { "action": "classify", "value": "{ask:classification}" } },
    { "title": "Resumo PT/EN na discussion", "kind": "devops-action", "params": { "action": "summary", "value": "" } },
    { "title": "Zerar o Remaining Work", "kind": "devops-action", "params": { "action": "zero-remaining", "value": "" } },
    { "title": "Mover para o Ready for QA da US", "kind": "devops-action", "params": { "action": "ready-for-qa", "value": "" } }
  ]
}
""";

        private static string Swap(bool up) => $$"""
            DO $mig$
            DECLARE
                r record; opts jsonb; tpls jsonb; changed boolean;
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
                        tpls := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTemplates', '')), '')::jsonb;
                    EXCEPTION WHEN others THEN
                        CONTINUE;
                    END;
                    changed := false;

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
