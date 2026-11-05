using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0085 (rodada 5): modelos de itens de trabalho sem "correção" para User Story e script sempre com chamado — idempotente,
    /// troca só o que ainda está EXATAMENTE como foi semeado (o que o admin editou fica):
    /// <list type="bullet">
    /// <item><c>script-com-chamado</c>: vale para Bug e US, título "Script de dados com chamado" e duas sub-tarefas de chamado
    /// (o Claude gera o chamado completo — arquivo do chamado — e a pessoa abre no sistema de chamados e cola o link);</item>
    /// <item><c>novo-resumo</c> fica só no Bug (tem root cause) e entra <c>novo-resumo-us</c> (US, sem root cause);</item>
    /// <item><c>validar-em-qa</c>, <c>corrigir-sonar</c>, <c>pr-release-version</c> e <c>pr-hotfix-version</c>: textos sem
    /// "correção".</item>
    /// </list>
    /// Down volta só o que ainda está como esta migração deixou.
    /// </summary>
    public partial class SeedUsTerms0085d : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        /// <summary>Modelos semeados pela <c>SeedSpecialistTools0085c</c> (texto exato) → versão nova.</summary>
        public static readonly IReadOnlyDictionary<string, (string Old, string New)> Replacements = BuildReplacements();

        /// <summary>Descrições semeadas pela <c>SeedWorkItems0085</c> (texto exato) → nova.</summary>
        public static readonly IReadOnlyDictionary<string, (string Old, string New)> Descriptions = new Dictionary<string, (string, string)>
        {
            ["pr-release-version"] = (
                "Leva a correção do card para a release-version: branch {branch}{suffix:release-version} a partir da release-version, cherry-pick dos commits do card e PR para a release-version.",
                "Leva os commits do card para a release-version: branch {branch}{suffix:release-version} a partir da release-version, cherry-pick dos commits do card e PR para a release-version."),
            ["pr-hotfix-version"] = (
                "Leva a correção do card para a hotfix-version: branch {branch}{suffix:hotfix-version} a partir da hotfix-version, cherry-pick dos commits do card e PR para a hotfix-version.",
                "Leva os commits do card para a hotfix-version: branch {branch}{suffix:hotfix-version} a partir da hotfix-version, cherry-pick dos commits do card e PR para a hotfix-version.")
        };

        /// <summary>Modelos novos (entram se a key faltar).</summary>
        public const string NewTemplates = """
[
  {
    "key": "novo-resumo-us", "type": "text", "group": "Textos",
    "title": "Novo resumo PT/EN no card",
    "description": "Publica um novo resumo não técnico (PT/EN) da User Story com o contexto informado, mantendo os anteriores.",
    "when": { "type": ["user-story"] },
    "tasks": [
      { "title": "Resumo PT/EN com o contexto novo", "kind": "devops-action", "params": { "action": "summary", "value": "" } }
    ]
  }
]
""";

        private static Dictionary<string, (string Old, string New)> BuildReplacements()
        {
            var seeded = JsonNode.Parse(SeedSpecialistTools0085c.NewTemplates)!.AsArray().OfType<JsonObject>()
                .ToDictionary(t => (string)t["key"]!, t => t);
            var map = new Dictionary<string, (string, string)>();
            void Change(string key, Action<JsonObject> change)
            {
                var old = seeded[key];
                var updated = (JsonObject)old.DeepClone();
                change(updated);
                map[key] = (old.ToJsonString(), updated.ToJsonString());
            }

            Change("script-com-chamado", t =>
            {
                t["title"] = "Script de dados com chamado";
                t["description"] = "Consulta antes (somente leitura), script com rollback, chamado completo gerado pelo Claude (arquivo do chamado), " +
                                   "aberto no {ticketSystem} por uma pessoa, e conferência depois.";
                t["when"] = new JsonObject { ["type"] = new JsonArray("bug", "user-story") };
                t["tasks"] = JsonNode.Parse("""
                [
                  { "title": "Consultar o estado atual (somente leitura)", "kind": "sql-query", "params": { "database": "", "how": "" } },
                  { "title": "Preparar o script com rollback", "kind": "sql-script", "params": { "database": "", "file": "" } },
                  { "title": "Gerar o chamado completo (arquivo do chamado)", "kind": "ticket", "executor": "claude",
                    "params": { "system": "{ticketSystem}", "title": "AB#{card} ", "description": "", "priority": "Média" } },
                  { "title": "Abrir o chamado no {ticketSystem} e colar o link", "kind": "ticket", "executor": "user",
                    "params": { "system": "{ticketSystem}" } },
                  { "title": "Conferir os dados depois do script", "kind": "sql-query", "params": { "database": "", "how": "" } }
                ]
                """);
            });
            Change("novo-resumo", t => t["when"] = new JsonObject { ["type"] = new JsonArray("bug") });
            Change("validar-em-qa", t => t["description"] =
                "Uma pessoa confere no ambiente de QA o que foi feito no card; aprovado, o card vai para Ready for QA (In Development (done)).");
            Change("corrigir-sonar", t => t["tasks"]![2]!["title"] = "Levar o ajuste às branches dos PRs abertos");
            return map;
        }

        /// <summary>{key: {old, new}} — valores JSON (<paramref name="text"/> = false) ou textos.</summary>
        private static string Pairs(IReadOnlyDictionary<string, (string Old, string New)> map, bool text) =>
            new JsonObject(map.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)new JsonObject
            {
                ["old"] = text ? JsonValue.Create(kv.Value.Old) : JsonNode.Parse(kv.Value.Old),
                ["new"] = text ? JsonValue.Create(kv.Value.New) : JsonNode.Parse(kv.Value.New)
            }))).ToJsonString();

        private static string Swap(bool up) => $$"""
            DO $mig$
            DECLARE
                r record; opts jsonb; tpls jsonb; tpls0 jsonb; nt jsonb;
                repl jsonb := $rp${{Pairs(Replacements, false)}}$rp$::jsonb;
                descs jsonb := $ds${{Pairs(Descriptions, true)}}$ds$::jsonb;
                new_tpls jsonb := $nt${{NewTemplates}}$nt$::jsonb;
                a text := '{{(up ? "old" : "new")}}';
                b text := '{{(up ? "new" : "old")}}';
            BEGIN
                FOR r IN
                    SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                    JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                    WHERE p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted" AND NULLIF(c."Options", '') IS NOT NULL
                LOOP
                    BEGIN
                        opts := r."Options"::jsonb;
                        tpls := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTemplates', '')), '')::jsonb;
                    EXCEPTION WHEN others THEN
                        CONTINUE;
                    END;
                    IF tpls IS NULL OR jsonb_typeof(tpls) <> 'array' THEN CONTINUE; END IF;
                    tpls0 := tpls;

                    SELECT COALESCE(jsonb_agg(
                        CASE
                            WHEN jsonb_typeof(t.e) <> 'object' THEN t.e
                            WHEN repl ? (t.e ->> 'key') AND t.e = repl -> (t.e ->> 'key') -> a THEN repl -> (t.e ->> 'key') -> b
                            WHEN descs ? (t.e ->> 'key') AND t.e -> 'description' = descs -> (t.e ->> 'key') -> a
                                THEN jsonb_set(t.e, '{description}', descs -> (t.e ->> 'key') -> b)
                            ELSE t.e
                        END ORDER BY t.o), '[]'::jsonb)
                    INTO tpls
                    FROM jsonb_array_elements(tpls) WITH ORDINALITY AS t(e, o)
                    WHERE {{(up ? "true" : "NOT EXISTS (SELECT 1 FROM jsonb_array_elements(new_tpls) n WHERE n = t.e)")}};

                    IF {{(up ? "true" : "false")}} THEN
                        FOR nt IN SELECT e FROM jsonb_array_elements(new_tpls) e LOOP
                            IF NOT EXISTS (SELECT 1 FROM jsonb_array_elements(tpls) x WHERE x ->> 'key' = nt ->> 'key') THEN
                                tpls := tpls || jsonb_build_array(nt);
                            END IF;
                        END LOOP;
                    END IF;

                    IF tpls IS DISTINCT FROM tpls0 THEN
                        opts := jsonb_set(opts, '{0,WorkItemTemplates}', to_jsonb(jsonb_pretty(tpls)), true);
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
