using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0099 (ajustes 2 — histórias e bugs na Mesa de gestão): <c>DeskStoryFieldMap</c> no "Skills Configurations" — para onde vai cada
    /// parte da história/bug ao virar card no DevOps. Idempotente e só acrescenta: põe a chave só se ela faltar (ou estiver vazia);
    /// JSON inválido do admin: a linha fica como está. Down tira só o que ainda está exatamente como esta migração pôs.
    /// </summary>
    public partial class SeedDeskStoryFieldMap0099 : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";

        /// <summary>O mesmo de <c>solvace.prform.Execution.DeskStoryFieldMap.DefaultJson</c> (API).</summary>
        public const string FieldMap = """
{
  "user-story": { "type": "User Story", "description": "System.Description", "acceptanceCriteria": "Microsoft.VSTS.Common.AcceptanceCriteria" },
  "bug": { "type": "Bug", "description": "System.Description", "reproSteps": "Microsoft.VSTS.TCM.ReproSteps" }
}
""";

        private static string Swap(bool up) => $$"""
            DO $mig$
            DECLARE
                r record; opts jsonb; cur text; changed boolean;
                fm jsonb := $fm${{FieldMap}}$fm$::jsonb;
            BEGIN
                FOR r IN
                    SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                    JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                    WHERE p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted" AND NULLIF(c."Options", '') IS NOT NULL
                LOOP
                    BEGIN
                        opts := r."Options"::jsonb;
                        IF jsonb_typeof(opts) <> 'array' OR jsonb_typeof(opts -> 0) <> 'object' THEN CONTINUE; END IF;
                        cur := opts -> 0 ->> 'DeskStoryFieldMap';
                    EXCEPTION WHEN others THEN
                        CONTINUE;
                    END;
                    changed := false;

                    IF {{(up ? "true" : "false")}} THEN
                        IF cur IS NULL OR btrim(cur) = '' THEN
                            opts := jsonb_set(opts, '{0,DeskStoryFieldMap}', to_jsonb(jsonb_pretty(fm)), true); changed := true;
                        END IF;
                    ELSIF cur IS NOT NULL THEN
                        BEGIN
                            IF cur::jsonb = fm THEN
                                opts := jsonb_set(opts, '{0}', (opts -> 0) - 'DeskStoryFieldMap'); changed := true;
                            END IF;
                        EXCEPTION WHEN others THEN
                            NULL;
                        END;
                    END IF;

                    IF changed THEN
                        UPDATE prform."PluginConfigurations" SET "Options" = opts::text WHERE "Id" = r."Id";
                    END IF;
                END LOOP;
            END
            $mig$;
            """;

        /// <summary>SQL do Up (para o teste conferir a montagem).</summary>
        public static string UpSql => Swap(true);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Swap(true));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Swap(false));
    }
}
