using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// 0085 (rodada 6): QA só onde o fluxo tem QA — idempotente e só acrescenta. O modelo <c>validar-em-qa</c> passa a valer só
    /// quando o fluxo do repositório do card tem PR para <c>qa</c> (<c>when.flowTargets = ["qa"]</c>): revamp-backend não tem QA
    /// (só development), então a ferramenta "Validar em QA" não aparece para ele. Só entra se o modelo ainda não tiver
    /// <c>when.flowTargets</c> (o que o admin já definiu fica). Também semeia <c>BranchNamePatternUserStory</c>
    /// (<c>feature/{card}</c>) se faltar: User Story é feature/, nunca hotfix/. JSON inválido do admin: a linha fica como está.
    /// Down tira só o que esta migração pôs (<c>flowTargets = ["qa"]</c> e o padrão <c>feature/{card}</c>).
    /// </summary>
    public partial class QaOnlyWhereFlowHasIt0085e : Migration
    {
        private const string SkillsPlugin = "Skills Configurations";
        private const string UsPattern = "feature/{card}";
        private const string UsPatternLabel = "Nome da branch da User Story ({card}) — feature/, nunca hotfix/";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DO $mig$
                DECLARE
                    r record; opts jsonb; tpls jsonb; changed boolean;
                BEGIN
                    FOR r IN
                        SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                        JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                        WHERE p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted" AND NULLIF(c."Options", '') IS NOT NULL
                    LOOP
                        BEGIN
                            opts := r."Options"::jsonb;
                            IF jsonb_typeof(opts) <> 'array' OR jsonb_array_length(opts) = 0 THEN CONTINUE; END IF;
                        EXCEPTION WHEN others THEN
                            CONTINUE;
                        END;

                        changed := false;
                        IF NOT (opts -> 0) ? 'BranchNamePatternUserStory' THEN
                            opts := jsonb_set(opts, '{0,BranchNamePatternUserStory}', to_jsonb($up${{UsPattern}}$up$::text), true);
                            changed := true;
                        END IF;

                        BEGIN
                            tpls := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTemplates', '')), '')::jsonb;
                        EXCEPTION WHEN others THEN
                            tpls := NULL;
                        END;
                        IF tpls IS NOT NULL AND jsonb_typeof(tpls) = 'array' AND EXISTS (
                            SELECT 1 FROM jsonb_array_elements(tpls) t
                            WHERE t ->> 'key' = 'validar-em-qa' AND NOT (COALESCE(t -> 'when', '{}'::jsonb) ? 'flowTargets')) THEN
                            tpls := (SELECT jsonb_agg(
                                CASE WHEN t ->> 'key' = 'validar-em-qa' AND NOT (COALESCE(t -> 'when', '{}'::jsonb) ? 'flowTargets')
                                     THEN jsonb_set(t, '{when}', COALESCE(t -> 'when', '{}'::jsonb) || '{"flowTargets": ["qa"]}'::jsonb, true)
                                     ELSE t END ORDER BY ord)
                                FROM jsonb_array_elements(tpls) WITH ORDINALITY AS e(t, ord));
                            opts := jsonb_set(opts, '{0,WorkItemTemplates}', to_jsonb(tpls::text), true);
                            changed := true;
                        END IF;

                        IF changed THEN
                            UPDATE prform."PluginConfigurations" SET "Options" = opts::text WHERE "Id" = r."Id";
                        END IF;
                    END LOOP;
                END
                $mig$;

                UPDATE prform."Plugins"
                SET "FieldSettings" = jsonb_set(COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb),
                        '{BranchNamePatternUserStory}', jsonb_build_object('Label', $lb${{UsPatternLabel}}$lb$::text), true)::text,
                    "UpdatedAt" = now()
                WHERE "Description" = '{{SkillsPlugin}}' AND NOT "IsDeleted"
                  AND NOT (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) ? 'BranchNamePatternUserStory');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DO $mig$
                DECLARE
                    r record; opts jsonb; tpls jsonb;
                BEGIN
                    FOR r IN
                        SELECT c."Id", c."Options" FROM prform."PluginConfigurations" c
                        JOIN prform."Plugins" p ON p."Id" = c."PluginId"
                        WHERE p."Description" = '{{SkillsPlugin}}' AND NOT p."IsDeleted" AND NULLIF(c."Options", '') IS NOT NULL
                    LOOP
                        BEGIN
                            opts := r."Options"::jsonb;
                            IF jsonb_typeof(opts) <> 'array' OR jsonb_array_length(opts) = 0 THEN CONTINUE; END IF;
                        EXCEPTION WHEN others THEN
                            CONTINUE;
                        END;

                        IF (opts -> 0 ->> 'BranchNamePatternUserStory') = $up${{UsPattern}}$up$ THEN
                            opts := jsonb_set(opts, '{0}', (opts -> 0) - 'BranchNamePatternUserStory', false);
                        END IF;

                        BEGIN
                            tpls := NULLIF(btrim(COALESCE(opts -> 0 ->> 'WorkItemTemplates', '')), '')::jsonb;
                        EXCEPTION WHEN others THEN
                            tpls := NULL;
                        END;
                        IF tpls IS NOT NULL AND jsonb_typeof(tpls) = 'array' THEN
                            tpls := (SELECT jsonb_agg(
                                CASE WHEN t ->> 'key' = 'validar-em-qa' AND (t -> 'when' -> 'flowTargets') = '["qa"]'::jsonb
                                     THEN jsonb_set(t, '{when}', (t -> 'when') - 'flowTargets', true)
                                     ELSE t END ORDER BY ord)
                                FROM jsonb_array_elements(tpls) WITH ORDINALITY AS e(t, ord));
                            opts := jsonb_set(opts, '{0,WorkItemTemplates}', to_jsonb(tpls::text), true);
                        END IF;

                        UPDATE prform."PluginConfigurations" SET "Options" = opts::text WHERE "Id" = r."Id";
                    END LOOP;
                END
                $mig$;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - 'BranchNamePatternUserStory')::text
                WHERE "Description" = '{{SkillsPlugin}}';
                """);
        }
    }
}
