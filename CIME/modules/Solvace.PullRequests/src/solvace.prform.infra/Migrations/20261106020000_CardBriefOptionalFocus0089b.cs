using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0089 (ajuste): no modelo <c>resumo-do-pedido</c>, "O que destacar" é opcional (<c>required: false</c>) — a tela trata a
    /// pergunta sem <c>required</c> como obrigatória e não liberava "Criar item" — e o modelo e o tipo <c>card-brief</c> passam a
    /// descrever um racional corrido (o que se pede/reclama, o que fazer e o estado atual), sem formato fixo. Troca só o que
    /// ainda for EXATAMENTE o semeado pela <c>SeedCardBrief0089</c> (o que o admin editou fica). Down volta só o que ainda está
    /// como esta migração deixou.
    /// </summary>
    public partial class CardBriefOptionalFocus0089b : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        public const string TemplateDescription =
            "Lê tudo do card — campos, descrição/repro steps, critérios de aceite, discussion, anexos e prints, planos, PRs e itens — e " +
            "escreve, em português (mesmo com o card em inglês), um racional curto do que está sendo pedido (User Story) ou reclamado " +
            "(Bug), do que precisa ser feito e do estado atual. Fica nos arquivos do card (resumo-do-pedido.md).";

        public const string KindDescription =
            "Racional do card, em português: o que está sendo pedido (User Story) ou reclamado (Bug), o que precisa ser feito e o estado " +
            "atual — lendo campos, descrição/repro steps, critérios de aceite, discussion, evidências, planos, PRs e itens. Só leitura; " +
            "publicar na Timeline é opcional.";

        /// <summary>Modelo novo: o semeado com <c>ask.focus.required = false</c> e a descrição nova.</summary>
        public static readonly string NewTemplate = BuildNewTemplate();

        /// <summary>Tipo novo: o semeado com a descrição nova.</summary>
        public static readonly string NewKind = BuildNewKind();

        private static string BuildNewTemplate()
        {
            var t = JsonNode.Parse(SeedCardBrief0089.NewTemplate)!.AsObject();
            t["description"] = TemplateDescription;
            foreach (var ask in t["ask"]!.AsArray().OfType<JsonObject>())
                if ((string)ask["key"]! == "focus")
                    ask["required"] = false;
            return t.ToJsonString();
        }

        private static string BuildNewKind()
        {
            var k = JsonNode.Parse(SeedCardBrief0089.NewKind)!.AsObject();
            k["description"] = KindDescription;
            return k.ToJsonString();
        }

        private static string Swap(bool up) => $$"""
            DO $mig$
            DECLARE
                r record; opts jsonb; opts0 jsonb; tpls jsonb; kinds jsonb;
                a jsonb := $a${{(up ? SeedCardBrief0089.NewTemplate : NewTemplate)}}$a$::jsonb;
                b jsonb := $b${{(up ? NewTemplate : SeedCardBrief0089.NewTemplate)}}$b$::jsonb;
                ka jsonb := $ka${{(up ? SeedCardBrief0089.NewKind : NewKind)}}$ka$::jsonb;
                kb jsonb := $kb${{(up ? NewKind : SeedCardBrief0089.NewKind)}}$kb$::jsonb;
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
                        kinds := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTaskKinds', '')), '')::jsonb;
                    EXCEPTION WHEN others THEN
                        CONTINUE;
                    END;
                    opts0 := opts;
                    IF tpls IS NOT NULL AND jsonb_typeof(tpls) = 'array' AND EXISTS (SELECT 1 FROM jsonb_array_elements(tpls) x WHERE x = a) THEN
                        SELECT jsonb_agg(CASE WHEN t.e = a THEN b ELSE t.e END ORDER BY t.o) INTO tpls
                        FROM jsonb_array_elements(tpls) WITH ORDINALITY AS t(e, o);
                        opts := jsonb_set(opts, '{0,WorkItemTemplates}', to_jsonb(jsonb_pretty(tpls)), true);
                    END IF;
                    IF kinds IS NOT NULL AND jsonb_typeof(kinds) = 'array' AND EXISTS (SELECT 1 FROM jsonb_array_elements(kinds) x WHERE x = ka) THEN
                        SELECT jsonb_agg(CASE WHEN t.e = ka THEN kb ELSE t.e END ORDER BY t.o) INTO kinds
                        FROM jsonb_array_elements(kinds) WITH ORDINALITY AS t(e, o);
                        opts := jsonb_set(opts, '{0,WorkItemTaskKinds}', to_jsonb(jsonb_pretty(kinds)), true);
                    END IF;
                    IF opts IS DISTINCT FROM opts0 THEN
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
