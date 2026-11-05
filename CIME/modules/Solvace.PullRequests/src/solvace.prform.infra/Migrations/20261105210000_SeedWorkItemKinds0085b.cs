using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0085 (rodada 3): itens de trabalho no "Skills Configurations" — <b>acrescenta</b> ao <c>WorkItemTaskKinds</c> os tipos
    /// que faltam (<c>ticket</c> = abrir chamado, <c>notify</c> = avisar na Timeline/Teams), sem mexer nos que o admin já tem;
    /// dá a cada param sem <c>input</c> o tipo do campo do editor (<c>repository|branch|target-branch|state|commits|text|
    /// textarea|select</c>, com <c>options</c> no select) — param que já tem <c>input</c> fica como está — e acrescenta o
    /// modelo <c>abrir-chamado</c> ao <c>WorkItemTemplates</c> se ele não existir. JSON inválido do admin: a linha fica como está.
    /// O mapeamento key → input é o mesmo do <c>WorkItemParamInputs</c> (catálogo) — mude os dois.
    /// </summary>
    public partial class SeedWorkItemKinds0085b : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        private const string TicketDescription =
            "Abre o chamado pelo fluxo de chamado do PRMake (texto do chamado nos arquivos do plano); quem acompanha a resolução é uma pessoa.";

        private const string TicketParams = """
[
  { "key": "title", "label": "Título do chamado", "required": true, "input": "text" },
  { "key": "description", "label": "Descrição", "required": true, "input": "textarea" },
  { "key": "priority", "label": "Prioridade", "input": "select", "options": ["Baixa", "Média", "Alta"] }
]
""";

        private const string NotifyKind = """
{
  "kind": "notify", "label": "Avisar (Timeline/Teams)", "icon": "campaign", "executor": "claude",
  "description": "Avisa no card: entrada na Timeline do PRMake e, se o Teams estiver configurado, mensagem no Teams.",
  "params": [
    { "key": "message", "label": "Mensagem", "required": true, "input": "textarea" }
  ]
}
""";

        private const string TicketTemplate = """
{
  "key": "abrir-chamado",
  "title": "Abrir chamado",
  "description": "Abre um chamado para o card {card} no sistema de chamados, pelo fluxo de chamado do PRMake.",
  "tasks": [
    { "title": "Abrir o chamado", "kind": "ticket",
      "params": { "system": "", "title": "AB#{card} ", "description": "", "priority": "Média" } }
  ]
}
""";

        private const string OldKindsLabel =
            "Itens de trabalho: tipos de sub-tarefa (JSON: [{kind, label, icon, executor, params[{key, label}], undo}])";

        private const string NewKindsLabel =
            "Itens de trabalho: tipos de sub-tarefa (JSON: [{kind, label, icon, executor, params[{key, label, required?, input?, options?}], undo}]; input = repository|branch|target-branch|state|commits|text|textarea|select)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DO $mig$
                DECLARE
                    r record; opts jsonb; orig jsonb; kinds jsonb; kinds0 jsonb; tpls jsonb; tpls0 jsonb;
                    ticket text; first_system text; sys_param jsonb;
                BEGIN
                    FOR r IN
                        SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                        JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                        WHERE p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted" AND NULLIF(c."Options", '') IS NOT NULL
                    LOOP
                        BEGIN
                            opts := r."Options"::jsonb;
                        EXCEPTION WHEN others THEN
                            CONTINUE;
                        END;
                        IF jsonb_typeof(opts) <> 'array' OR jsonb_typeof(opts -> 0) <> 'object' THEN CONTINUE; END IF;
                        orig := opts;

                        ticket := btrim(COALESCE(opts -> 0 ->> 'TicketSystem', ''));
                        first_system := btrim(COALESCE((regexp_split_to_array(ticket, '[,;]'))[1], ''));
                        sys_param := CASE WHEN ticket = ''
                            THEN jsonb_build_object('key', 'system', 'label', 'Sistema de chamados', 'input', 'text')
                            ELSE jsonb_build_object('key', 'system', 'label', 'Sistema de chamados', 'input', 'select', 'options',
                                (SELECT COALESCE(jsonb_agg(btrim(x)), '[]'::jsonb)
                                 FROM unnest(regexp_split_to_array(ticket, '[,;]')) AS x WHERE btrim(x) <> ''))
                        END;

                        -- Tipos de sub-tarefa: acrescenta os que faltam e o input dos params que não têm.
                        BEGIN
                            kinds := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTaskKinds', '')), '')::jsonb;
                        EXCEPTION WHEN others THEN
                            kinds := NULL;
                        END;
                        IF kinds IS NOT NULL AND jsonb_typeof(kinds) = 'array' THEN
                            kinds0 := kinds;
                            IF NOT EXISTS (SELECT 1 FROM jsonb_array_elements(kinds) e WHERE e ->> 'kind' = 'ticket') THEN
                                kinds := kinds || jsonb_build_array(jsonb_build_object(
                                    'kind', 'ticket', 'label', 'Abrir chamado', 'icon', 'confirmation_number', 'executor', 'claude',
                                    'description', $td${{TicketDescription}}$td$::text,
                                    'params', jsonb_build_array(sys_param) || $tp${{TicketParams}}$tp$::jsonb));
                            END IF;
                            IF NOT EXISTS (SELECT 1 FROM jsonb_array_elements(kinds) e WHERE e ->> 'kind' = 'notify') THEN
                                kinds := kinds || jsonb_build_array($nk${{NotifyKind}}$nk$::jsonb);
                            END IF;

                            SELECT COALESCE(jsonb_agg(
                                CASE WHEN jsonb_typeof(t.k) = 'object' AND jsonb_typeof(t.k -> 'params') = 'array' THEN
                                    jsonb_set(t.k, '{params}', (
                                        SELECT COALESCE(jsonb_agg(
                                            CASE WHEN jsonb_typeof(q.pa) = 'object' AND NOT (q.pa ? 'input') THEN
                                                (CASE
                                                    WHEN q.pa ->> 'key' = 'repository' THEN '{"input": "repository"}'
                                                    WHEN q.pa ->> 'key' IN ('branch', 'source', 'from') THEN '{"input": "branch"}'
                                                    WHEN q.pa ->> 'key' = 'target' AND t.k ->> 'kind' = 'comment'
                                                        THEN '{"input": "select", "options": ["timeline", "devops"]}'
                                                    WHEN q.pa ->> 'key' = 'target' THEN '{"input": "target-branch"}'
                                                    WHEN q.pa ->> 'key' = 'state' THEN '{"input": "state"}'
                                                    WHEN q.pa ->> 'key' = 'commits' THEN '{"input": "commits"}'
                                                    WHEN q.pa ->> 'key' = 'draft' THEN '{"input": "select", "options": ["false", "true"]}'
                                                    WHEN q.pa ->> 'key' = 'priority' THEN '{"input": "select", "options": ["Baixa", "Média", "Alta"]}'
                                                    WHEN q.pa ->> 'key' IN ('description', 'instructions', 'message', 'text', 'how')
                                                        THEN '{"input": "textarea"}'
                                                    ELSE '{"input": "text"}'
                                                END)::jsonb || q.pa
                                            ELSE q.pa END ORDER BY q.po), '[]'::jsonb)
                                        FROM jsonb_array_elements(t.k -> 'params') WITH ORDINALITY AS q(pa, po)))
                                ELSE t.k END ORDER BY t.ko), '[]'::jsonb)
                            INTO kinds
                            FROM jsonb_array_elements(kinds) WITH ORDINALITY AS t(k, ko);

                            IF kinds IS DISTINCT FROM kinds0 THEN
                                opts := jsonb_set(opts, '{0,WorkItemTaskKinds}', to_jsonb(jsonb_pretty(kinds)), true);
                            END IF;
                        END IF;

                        -- Modelos: acrescenta o abrir-chamado se não houver.
                        BEGIN
                            tpls := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTemplates', '')), '')::jsonb;
                        EXCEPTION WHEN others THEN
                            tpls := NULL;
                        END;
                        IF tpls IS NOT NULL AND jsonb_typeof(tpls) = 'array'
                           AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(tpls) e WHERE e ->> 'key' = 'abrir-chamado') THEN
                            tpls0 := tpls;
                            tpls := tpls || jsonb_build_array(
                                jsonb_set($tt${{TicketTemplate}}$tt$::jsonb, '{tasks,0,params,system}', to_jsonb(first_system), true));
                            IF tpls IS DISTINCT FROM tpls0 THEN
                                opts := jsonb_set(opts, '{0,WorkItemTemplates}', to_jsonb(jsonb_pretty(tpls)), true);
                            END IF;
                        END IF;

                        IF opts IS DISTINCT FROM orig THEN
                            UPDATE prform."PluginConfigurations" SET "Options" = opts::text WHERE "Id" = r."Id";
                        END IF;
                    END LOOP;
                END
                $mig$;

                UPDATE prform."Plugins"
                SET "FieldSettings" = jsonb_set("FieldSettings"::jsonb, '{WorkItemTaskKinds,Label}', to_jsonb($nl${{NewKindsLabel}}$nl$::text), true)::text,
                    "UpdatedAt" = now()
                WHERE "Description" = '{{SkillsPlugin}}' AND NOT "IsDeleted" AND NULLIF("FieldSettings", '') IS NOT NULL
                  AND ("FieldSettings"::jsonb -> 'WorkItemTaskKinds' ->> 'Label') = $ol${{OldKindsLabel}}$ol$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tira só o que esta migração acrescentou (tipos ticket/notify e o modelo abrir-chamado); o input dos params fica.
            migrationBuilder.Sql($$"""
                DO $mig$
                DECLARE
                    r record; opts jsonb; kinds jsonb; tpls jsonb;
                BEGIN
                    FOR r IN
                        SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                        JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                        WHERE p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL
                    LOOP
                        BEGIN
                            opts := r."Options"::jsonb;
                            kinds := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTaskKinds', '')), '')::jsonb;
                            tpls := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTemplates', '')), '')::jsonb;
                        EXCEPTION WHEN others THEN
                            CONTINUE;
                        END;
                        IF kinds IS NOT NULL AND jsonb_typeof(kinds) = 'array' THEN
                            SELECT COALESCE(jsonb_agg(e ORDER BY o), '[]'::jsonb) INTO kinds
                            FROM jsonb_array_elements(kinds) WITH ORDINALITY AS t(e, o)
                            WHERE COALESCE(e ->> 'kind', '') NOT IN ('ticket', 'notify');
                            opts := jsonb_set(opts, '{0,WorkItemTaskKinds}', to_jsonb(jsonb_pretty(kinds)), true);
                        END IF;
                        IF tpls IS NOT NULL AND jsonb_typeof(tpls) = 'array' THEN
                            SELECT COALESCE(jsonb_agg(e ORDER BY o), '[]'::jsonb) INTO tpls
                            FROM jsonb_array_elements(tpls) WITH ORDINALITY AS t(e, o)
                            WHERE COALESCE(e ->> 'key', '') <> 'abrir-chamado';
                            opts := jsonb_set(opts, '{0,WorkItemTemplates}', to_jsonb(jsonb_pretty(tpls)), true);
                        END IF;
                        UPDATE prform."PluginConfigurations" SET "Options" = opts::text WHERE "Id" = r."Id";
                    END LOOP;
                END
                $mig$;

                UPDATE prform."Plugins"
                SET "FieldSettings" = jsonb_set("FieldSettings"::jsonb, '{WorkItemTaskKinds,Label}', to_jsonb($ol${{OldKindsLabel}}$ol$::text), true)::text
                WHERE "Description" = '{{SkillsPlugin}}' AND NULLIF("FieldSettings", '') IS NOT NULL
                  AND ("FieldSettings"::jsonb -> 'WorkItemTaskKinds' ->> 'Label') = $nl${{NewKindsLabel}}$nl$;
                """);
        }
    }
}
