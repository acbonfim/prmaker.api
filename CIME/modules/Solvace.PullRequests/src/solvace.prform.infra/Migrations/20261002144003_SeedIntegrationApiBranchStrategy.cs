using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0048: o repositório da API de integrações (<c>edv-solvace-api</c>) não casava com nenhuma regra do
    /// <c>BranchStrategy</c> ("Skills Configurations") — a busca dos repositórios da máquina não o acharia e o
    /// <c>branches</c> da skill parava com "sem regra". Acrescenta a regra (tipo <c>integration-api</c>, logo após a do
    /// <c>edv-solvace-apps</c>) e o fluxo desse tipo em <c>producao</c>/<c>release</c> (base perguntada: master ou
    /// release-version) — só o que falta; o que o admin já editou é preservado. JSON inválido no campo: não toca.
    /// </summary>
    public partial class SeedIntegrationApiBranchStrategy : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";
        private const string Rule = """{"match": "edv-solvace-api", "kind": "integration-api"}""";
        private const string Flow = """{"askBase": true, "baseOptions": ["master", "release-version"], "prs": [{"suffix": "", "target": "{base}"}]}""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DO $mig$
                DECLARE
                    r record; opts jsonb; bs jsonb; orig jsonb; repos jsonb; flows jsonb; f text; pos numeric;
                BEGIN
                    FOR r IN
                        SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                        JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                        WHERE p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted" AND NULLIF(c."Options", '') IS NOT NULL
                    LOOP
                        BEGIN
                            opts := r."Options"::jsonb;
                            bs := (opts -> 0 ->> 'BranchStrategy')::jsonb;
                        EXCEPTION WHEN others THEN
                            CONTINUE;
                        END;
                        IF bs IS NULL OR jsonb_typeof(bs) <> 'object' THEN CONTINUE; END IF;
                        orig := bs;

                        repos := COALESCE(bs -> 'repositories', '[]'::jsonb);
                        IF jsonb_typeof(repos) = 'array' AND NOT EXISTS (
                            SELECT 1 FROM jsonb_array_elements(repos) e WHERE lower(e ->> 'match') = 'edv-solvace-api') THEN
                            SELECT COALESCE(max(t.ord), 0) INTO pos
                            FROM jsonb_array_elements(repos) WITH ORDINALITY AS t(e, ord)
                            WHERE lower(t.e ->> 'match') = 'edv-solvace-apps';
                            SELECT jsonb_agg(s.e ORDER BY s.ord) INTO repos FROM (
                                SELECT t.e, t.ord::numeric AS ord FROM jsonb_array_elements(repos) WITH ORDINALITY AS t(e, ord)
                                UNION ALL SELECT '{{Rule}}'::jsonb, pos + 0.5) s;
                            bs := jsonb_set(bs, '{repositories}', repos, true);
                        END IF;

                        flows := bs -> 'flows';
                        IF flows IS NOT NULL AND jsonb_typeof(flows) = 'object' THEN
                            FOREACH f IN ARRAY ARRAY['producao', 'release'] LOOP
                                IF jsonb_typeof(flows -> f) = 'object' AND NOT (flows -> f) ? 'integration-api' THEN
                                    flows := jsonb_set(flows, ARRAY[f, 'integration-api'], '{{Flow}}'::jsonb, true);
                                END IF;
                            END LOOP;
                            bs := jsonb_set(bs, '{flows}', flows, true);
                        END IF;

                        IF bs IS DISTINCT FROM orig THEN
                            UPDATE prform."PluginConfigurations"
                            SET "Options" = jsonb_set(opts, '{0,BranchStrategy}', to_jsonb(jsonb_pretty(bs)), true)::text
                            WHERE "Id" = r."Id";
                        END IF;
                    END LOOP;
                END
                $mig$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove só o que é exatamente o semeado (regra com o mesmo tipo e fluxos iguais aos da migração).
            migrationBuilder.Sql($$"""
                DO $mig$
                DECLARE
                    r record; opts jsonb; bs jsonb; orig jsonb; f text;
                BEGIN
                    FOR r IN
                        SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                        JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                        WHERE p."Description" = '{{SkillsPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL
                    LOOP
                        BEGIN
                            opts := r."Options"::jsonb;
                            bs := (opts -> 0 ->> 'BranchStrategy')::jsonb;
                        EXCEPTION WHEN others THEN
                            CONTINUE;
                        END;
                        IF bs IS NULL OR jsonb_typeof(bs) <> 'object' THEN CONTINUE; END IF;
                        orig := bs;
                        IF jsonb_typeof(bs -> 'repositories') = 'array' THEN
                            bs := jsonb_set(bs, '{repositories}', COALESCE((
                                SELECT jsonb_agg(t.e ORDER BY t.ord) FROM jsonb_array_elements(bs -> 'repositories') WITH ORDINALITY AS t(e, ord)
                                WHERE t.e <> '{{Rule}}'::jsonb), '[]'::jsonb));
                        END IF;
                        FOREACH f IN ARRAY ARRAY['producao', 'release'] LOOP
                            IF bs -> 'flows' -> f -> 'integration-api' = '{{Flow}}'::jsonb THEN
                                bs := bs #- ARRAY['flows', f, 'integration-api'];
                            END IF;
                        END LOOP;
                        IF bs IS DISTINCT FROM orig THEN
                            UPDATE prform."PluginConfigurations"
                            SET "Options" = jsonb_set(opts, '{0,BranchStrategy}', to_jsonb(jsonb_pretty(bs)), true)::text
                            WHERE "Id" = r."Id";
                        END IF;
                    END LOOP;
                END
                $mig$;
                """);
        }
    }
}
