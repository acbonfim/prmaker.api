using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// Tipos de sub-tarefa para as etapas do plano que não são código (antes todas viravam "Alterar código") — idempotente
    /// e só acrescenta ao <c>WorkItemTaskKinds</c> do "Skills Configurations" os que faltarem:
    /// <list type="bullet">
    /// <item><c>research</c>: análise/consulta sem alterar nada (impacto na base, ler o card, mapear o código, causa raiz);</item>
    /// <item><c>decision</c>: propor e decidir com a pessoa (perguntas da análise);</item>
    /// <item><c>task</c>: tarefa do Claude que não se encaixa em outro tipo.</item>
    /// </list>
    /// JSON inválido do admin: a linha fica como está. Down tira só o que ainda está exatamente como esta migração pôs.
    /// </summary>
    public partial class SeedStepTaskKinds : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        public const string NewKinds = """
[
  { "kind": "research", "label": "Análise / consulta (sem alterar nada)", "icon": "travel_explore", "executor": "claude",
    "description": "Ler e entender sem mudar nada: card, evidências, Base Solvace (impacto, regras), código e dados só leitura. O resultado fica no checkpoint e nos arquivos do plano.",
    "params": [] },
  { "kind": "decision", "label": "Decidir com você", "icon": "forum", "executor": "claude",
    "description": "Apresentar as opções (prós, contras, riscos) e perguntar — a pessoa responde no PRMake ou no Claude.",
    "params": [] },
  { "kind": "task", "label": "Tarefa", "icon": "task_alt", "executor": "claude",
    "description": "Tarefa do Claude que não é código, PR, script, consulta nem ação no DevOps.",
    "params": [] }
]
""";

        private static string Swap(bool up) => $$"""
            DO $mig$
            DECLARE
                r record; opts jsonb; kinds jsonb; changed boolean; nk jsonb;
                nks jsonb := $nk${{NewKinds}}$nk$::jsonb;
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
                    EXCEPTION WHEN others THEN
                        CONTINUE;
                    END;
                    IF kinds IS NULL OR jsonb_typeof(kinds) <> 'array' THEN CONTINUE; END IF;
                    changed := false;

                    FOR nk IN SELECT * FROM jsonb_array_elements(nks) LOOP
                        IF {{(up ? "true" : "false")}} AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(kinds) x WHERE x ->> 'kind' = nk ->> 'kind') THEN
                            kinds := kinds || jsonb_build_array(nk); changed := true;
                        ELSIF {{(up ? "false" : "true")}} AND EXISTS (SELECT 1 FROM jsonb_array_elements(kinds) x WHERE x = nk) THEN
                            SELECT COALESCE(jsonb_agg(e ORDER BY o), '[]'::jsonb) INTO kinds
                            FROM jsonb_array_elements(kinds) WITH ORDINALITY AS t(e, o) WHERE t.e <> nk;
                            changed := true;
                        END IF;
                    END LOOP;

                    IF changed THEN
                        opts := jsonb_set(opts, '{0,WorkItemTaskKinds}', to_jsonb(jsonb_pretty(kinds)), true);
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
