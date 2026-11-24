using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0100: aba AWS na Bancada — idempotente e só acrescenta no "Skills Configurations":
    /// <list type="bullet">
    /// <item><c>AwsDeployEnvironments</c> (DEV, QA, Sandboxefeso, SandboxRT, Produção com o host de cada um) se a chave faltar;</item>
    /// <item><c>AwsDeployFollowMinutes</c> (2) e <c>AwsDeployFollowMax</c> (15) — acompanhamento do deploy em andamento — se faltarem;</item>
    /// <item>tipo de sub-tarefa <c>aws-deploy-map</c> se faltar.</item>
    /// </list>
    /// JSON inválido do admin: a linha fica como está. Down tira só o que ainda está exatamente como esta migração pôs.
    /// </summary>
    public partial class SeedAwsDeploy0100 : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        /// <summary>O mesmo de <c>solvace.prform.AwsDeploy.AwsDeploySettings.DefaultEnvironmentsJson</c> (API).</summary>
        public const string Environments = """
[
  { "key": "dev", "label": "DEV", "host": "devsp.solvacelabs.com", "order": 1 },
  { "key": "qa", "label": "QA", "host": "qa.solvacelabs.com", "order": 2 },
  { "key": "sandboxefeso", "label": "Sandboxefeso", "host": "sandboxefeso.solvacelabs.com", "order": 3 },
  { "key": "sandboxrt", "label": "SandboxRT", "host": "sandboxrt.solvacelabs.com", "order": 4 },
  { "key": "prod", "label": "Produção", "host": "efeso.solvacelabs.com", "order": 5 }
]
""";

        public const string FollowMinutes = "2";
        public const string FollowMax = "15";

        public const string NewKind = """
{ "kind": "aws-deploy-map", "label": "Mapear deploy na AWS", "icon": "cloud_sync", "executor": "claude",
  "description": "Mapeia as esteiras (pipelines) do card por ambiente e se o nosso código já está em cada um — somente leitura, coletado pela CLI da AWS da máquina de quem executa (bash ~/.claude/skills/aws-deploy/scripts/aws.sh collect <card>). O retrato fica na aba AWS da Bancada e no prmake_aws_status.",
  "params": [] }
""";

        private static string Swap(bool up) => $$"""
            DO $mig$
            DECLARE
                r record; opts jsonb; kinds jsonb; envs text; changed boolean;
                nk jsonb := $nk${{NewKind}}$nk$::jsonb;
                ne jsonb := $ne${{Environments}}$ne$::jsonb;
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
                        envs := opts -> 0 ->> 'AwsDeployEnvironments';
                    EXCEPTION WHEN others THEN
                        CONTINUE;
                    END;
                    changed := false;

                    IF {{(up ? "true" : "false")}} THEN
                        IF envs IS NULL OR btrim(envs) = '' THEN
                            opts := jsonb_set(opts, '{0,AwsDeployEnvironments}', to_jsonb(jsonb_pretty(ne)), true); changed := true;
                        END IF;
                        IF NULLIF(btrim(COALESCE(opts -> 0 ->> 'AwsDeployFollowMinutes', '')), '') IS NULL THEN
                            opts := jsonb_set(opts, '{0,AwsDeployFollowMinutes}', to_jsonb('{{FollowMinutes}}'::text), true); changed := true;
                        END IF;
                        IF NULLIF(btrim(COALESCE(opts -> 0 ->> 'AwsDeployFollowMax', '')), '') IS NULL THEN
                            opts := jsonb_set(opts, '{0,AwsDeployFollowMax}', to_jsonb('{{FollowMax}}'::text), true); changed := true;
                        END IF;
                    ELSE
                        IF envs IS NOT NULL THEN
                            BEGIN
                                IF envs::jsonb = ne THEN
                                    opts := jsonb_set(opts, '{0}', (opts -> 0) - 'AwsDeployEnvironments'); changed := true;
                                END IF;
                            EXCEPTION WHEN others THEN
                                NULL;
                            END;
                        END IF;
                        IF opts -> 0 ->> 'AwsDeployFollowMinutes' = '{{FollowMinutes}}' THEN
                            opts := jsonb_set(opts, '{0}', (opts -> 0) - 'AwsDeployFollowMinutes'); changed := true;
                        END IF;
                        IF opts -> 0 ->> 'AwsDeployFollowMax' = '{{FollowMax}}' THEN
                            opts := jsonb_set(opts, '{0}', (opts -> 0) - 'AwsDeployFollowMax'); changed := true;
                        END IF;
                    END IF;

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
