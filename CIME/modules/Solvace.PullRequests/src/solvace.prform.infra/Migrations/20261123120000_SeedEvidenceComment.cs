using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// Comentário com as evidências da validação — idempotente e só acrescenta no "Skills Configurations":
    /// <list type="bullet">
    /// <item>tipo de sub-tarefa <c>evidence-comment</c> (executor Claude: escreve o comentário com as evidências e grava nos
    /// arquivos do item; publica na discussion do card só com <c>discussion = sim</c>) se faltar;</item>
    /// <item>nos modelos <c>validar-em-ambiente</c> e <c>validar-em-qa</c>: a pergunta <c>discussion</c> ("Publicar ... na
    /// discussion?", padrão não) e a sub-tarefa <c>evidence-comment</c> (no fim; no QA, antes de mover para Ready for QA) se
    /// faltarem;</item>
    /// <item>no <c>validar-em-qa</c>: a validação manual sem instruções passa a pedir os prints na sub-tarefa.</item>
    /// </list>
    /// JSON inválido do admin: a linha fica como está. Down tira só o que ainda está exatamente como esta migração pôs.
    /// </summary>
    public partial class SeedEvidenceComment : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        public const string NewKind = """
{ "kind": "evidence-comment", "label": "Comentário com as evidências", "icon": "forum", "executor": "claude",
  "description": "Escreve o comentário com as evidências da validação (o que foi validado, onde, o resultado e os prints que provam) e grava nos arquivos do item. Publica na discussion do card no DevOps só quando \"Publicar na discussion\" = sim.",
  "params": [
    { "key": "discussion", "label": "Publicar na discussion", "input": "select", "options": ["não", "sim"] }
  ] }
""";

        public const string NewAsk = """
{ "key": "discussion", "label": "Publicar o comentário com as evidências na discussion do card?", "input": "select", "options": ["não", "sim"], "default": "não", "required": true }
""";

        public const string EnvTask = """
{ "title": "Comentário com as evidências", "kind": "evidence-comment", "params": { "discussion": "{ask:discussion}" } }
""";

        public const string QaTask = """
{ "title": "Comentário com as evidências da validação em QA", "kind": "evidence-comment", "params": { "discussion": "{ask:discussion}" } }
""";

        public const string QaInstructions =
            "Confira no ambiente de QA o que foi feito no card. Anexe nesta sub-tarefa os prints que mostram o resultado " +
            "(aba Arquivos) e escreva o resultado ao concluir — eles entram no comentário com as evidências.";

        private static string Swap(bool up) => $$"""
            DO $mig$
            DECLARE
                r record; opts jsonb; kinds jsonb; tpls jsonb; t jsonb; nt jsonb; tmp jsonb; changed boolean; i int; pos int; k text;
                nk jsonb := $nk${{NewKind}}$nk$::jsonb;
                na jsonb := $na${{NewAsk}}$na$::jsonb;
                te jsonb := $te${{EnvTask}}$te$::jsonb;
                tq jsonb := $tq${{QaTask}}$tq$::jsonb;
                qi text := $qi${{QaInstructions}}$qi$;
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
                            kinds := kinds || jsonb_build_array(nk);
                            opts := jsonb_set(opts, '{0,WorkItemTaskKinds}', to_jsonb(jsonb_pretty(kinds)), true); changed := true;
                        ELSIF {{(up ? "false" : "true")}} AND EXISTS (SELECT 1 FROM jsonb_array_elements(kinds) x WHERE x = nk) THEN
                            SELECT COALESCE(jsonb_agg(e ORDER BY o), '[]'::jsonb) INTO kinds
                            FROM jsonb_array_elements(kinds) WITH ORDINALITY AS x(e, o) WHERE x.e <> nk;
                            opts := jsonb_set(opts, '{0,WorkItemTaskKinds}', to_jsonb(jsonb_pretty(kinds)), true); changed := true;
                        END IF;
                    END IF;

                    IF tpls IS NOT NULL AND jsonb_typeof(tpls) = 'array' THEN
                        FOR i IN 0 .. jsonb_array_length(tpls) - 1 LOOP
                            t := tpls -> i;
                            k := t ->> 'key';
                            IF k IS NULL OR k NOT IN ('validar-em-ambiente', 'validar-em-qa') OR jsonb_typeof(t -> 'tasks') <> 'array'
                               OR jsonb_typeof(COALESCE(t -> 'ask', '[]'::jsonb)) <> 'array' THEN
                                CONTINUE;
                            END IF;
                            nt := t;

                            IF {{(up ? "true" : "false")}} THEN
                                IF NOT EXISTS (SELECT 1 FROM jsonb_array_elements(COALESCE(nt -> 'ask', '[]'::jsonb)) a WHERE a ->> 'key' = na ->> 'key') THEN
                                    nt := jsonb_set(nt, '{ask}', COALESCE(nt -> 'ask', '[]'::jsonb) || jsonb_build_array(na), true);
                                END IF;
                                IF NOT EXISTS (SELECT 1 FROM jsonb_array_elements(nt -> 'tasks') x WHERE x ->> 'kind' = nk ->> 'kind') THEN
                                    -- No QA o comentário vem antes de mover para Ready for QA; sem essa sub-tarefa, no fim.
                                    SELECT MIN(o) - 1 INTO pos FROM jsonb_array_elements(nt -> 'tasks') WITH ORDINALITY AS x(e, o)
                                    WHERE x.e -> 'params' ->> 'action' = 'ready-for-qa';
                                    IF k = 'validar-em-qa' AND pos IS NOT NULL THEN
                                        nt := jsonb_insert(nt, ARRAY['tasks', pos::text], tq);
                                    ELSE
                                        nt := jsonb_set(nt, '{tasks}', (nt -> 'tasks') || jsonb_build_array(CASE WHEN k = 'validar-em-qa' THEN tq ELSE te END));
                                    END IF;
                                END IF;
                                IF k = 'validar-em-qa' THEN
                                    SELECT jsonb_agg(CASE WHEN e ->> 'kind' = 'manual' AND COALESCE(e -> 'params' ->> 'instructions', '') = ''
                                                          THEN jsonb_set(e, '{params}', COALESCE(e -> 'params', '{}'::jsonb) || jsonb_build_object('instructions', qi), true)
                                                          ELSE e END ORDER BY o)
                                    INTO tmp FROM jsonb_array_elements(nt -> 'tasks') WITH ORDINALITY AS x(e, o);
                                    nt := jsonb_set(nt, '{tasks}', tmp);
                                END IF;
                            ELSE
                                SELECT COALESCE(jsonb_agg(e ORDER BY o), '[]'::jsonb) INTO tmp
                                FROM jsonb_array_elements(COALESCE(nt -> 'ask', '[]'::jsonb)) WITH ORDINALITY AS x(e, o) WHERE x.e <> na;
                                nt := CASE WHEN tmp = '[]'::jsonb THEN nt - 'ask' ELSE jsonb_set(nt, '{ask}', tmp) END;
                                SELECT COALESCE(jsonb_agg(CASE WHEN e ->> 'kind' = 'manual' AND e -> 'params' ->> 'instructions' = qi
                                                               THEN jsonb_set(e, '{params,instructions}', '""'::jsonb) ELSE e END ORDER BY o), '[]'::jsonb)
                                INTO tmp FROM jsonb_array_elements(nt -> 'tasks') WITH ORDINALITY AS x(e, o) WHERE x.e <> te AND x.e <> tq;
                                nt := jsonb_set(nt, '{tasks}', tmp);
                            END IF;

                            IF nt <> t THEN
                                tpls := jsonb_set(tpls, ARRAY[i::text], nt); changed := true;
                                opts := jsonb_set(opts, '{0,WorkItemTemplates}', to_jsonb(jsonb_pretty(tpls)), true);
                            END IF;
                        END LOOP;
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
