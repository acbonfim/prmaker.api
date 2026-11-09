using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0092b: rotas que podem gravar nos ambientes somente leitura (DEMO, produção) porque só guardam preferência/filtro do
    /// usuário — <c>readOnlyAllow</c> em <c>ValidationEnvironments</c>, semeado com <c>user-report-preferences</c> (filtro
    /// salvo dos relatórios do legado; validação do card 77698 na produção da Lear). Idempotente: só acrescenta a chave se
    /// faltar; JSON inválido do admin fica como está. Down tira só se ainda estiver exatamente como esta migração pôs.
    /// </summary>
    public partial class ValidationReadOnlyAllow0092b : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        public const string DefaultAllow = """["user-report-preferences"]""";

        private static string Swap(bool up) => $$"""
            DO $mig$
            DECLARE
                r record; opts jsonb; envs jsonb;
                na jsonb := '{{DefaultAllow}}'::jsonb;
            BEGIN
                FOR r IN
                    SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                    JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                    WHERE p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted" AND NULLIF(c."Options", '') IS NOT NULL
                LOOP
                    BEGIN
                        opts := r."Options"::jsonb;
                        IF jsonb_typeof(opts) <> 'array' OR jsonb_typeof(opts -> 0) <> 'object' THEN CONTINUE; END IF;
                        envs := NULLIF(btrim(COALESCE(opts -> 0 ->> 'ValidationEnvironments', '')), '')::jsonb;
                    EXCEPTION WHEN others THEN
                        CONTINUE;
                    END;
                    IF envs IS NULL OR jsonb_typeof(envs) <> 'object' THEN CONTINUE; END IF;
                    IF {{(up ? "true" : "false")}} AND NOT envs ? 'readOnlyAllow' THEN
                        envs := envs || jsonb_build_object('readOnlyAllow', na);
                    ELSIF {{(up ? "false" : "true")}} AND envs -> 'readOnlyAllow' = na THEN
                        envs := envs - 'readOnlyAllow';
                    ELSE
                        CONTINUE;
                    END IF;
                    opts := jsonb_set(opts, '{0,ValidationEnvironments}', to_jsonb(jsonb_pretty(envs)), true);
                    UPDATE prform."PluginConfigurations" SET "Options" = opts::text WHERE "Id" = r."Id";
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
