using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// Feature 0033: plugin "Knowledge Center Configurations" — ambiente ativo do KC (dev hoje; trocar para prod é
    /// mudar Environment), host/database/schema de cada ambiente (a credencial fica só na máquina de quem sincroniza)
    /// e as regras EXTRAS do filtro de teste (o piso é fixo no código). Idempotente: só acrescenta o que falta.
    /// </summary>
    public partial class SeedKnowledgeCenterConfigurations : Migration
    {
        private const string Plugin = "Knowledge Center Configurations";

        private const string Options = """
{"Environment": "dev", "DevHost": "solvace-pstgdev.cluster-ro-cjsrhvr5mbhe.us-east-1.rds.amazonaws.com", "DevDatabase": "KnowledgeCenter", "DevSchema": "knowledge_center", "ProdHost": "solvace-pstgprd.cluster-ro-cjsrhvr5mbhe.us-east-1.rds.amazonaws.com", "ProdDatabase": "KnowledgeCenter", "ProdSchema": "knowledge_center", "ExcludePatterns": "[]", "ExcludeArticles": "[]", "AllowArticles": "[]", "MinTextLength": "200", "FullSyncHours": "24"}
""";

        private const string FieldSettings = """
{"Environment": {"Label": "Ambiente do Knowledge Center (dev | prod)", "Help": "Trocar para prod: credencial 'prod' no ~/.claude/knowledgecenter-credentials.json de quem sincroniza; a próxima sincronização faz a carga completa."}, "DevHost": {"Label": "DEV: host do banco (somente leitura)"}, "DevDatabase": {"Label": "DEV: database"}, "DevSchema": {"Label": "DEV: schema"}, "ProdHost": {"Label": "PROD: host do banco (somente leitura)"}, "ProdDatabase": {"Label": "PROD: database"}, "ProdSchema": {"Label": "PROD: schema"}, "ExcludePatterns": {"Label": "Padrões extras de teste (JSON: lista de regex) — somados ao filtro fixo"}, "ExcludeArticles": {"Label": "Artigos sempre fora (JSON: [12, \"ART-15\"])"}, "AllowArticles": {"Label": "Artigos liberados apesar do filtro (JSON) — nunca rascunho/arquivado/removido"}, "MinTextLength": {"Label": "Texto mínimo do artigo (não vale abaixo de 200)"}, "FullSyncHours": {"Label": "Carga completa a cada N horas"}}
""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                INSERT INTO prform."Plugins" ("Description", "CreatedAt", "CreatedBy", "AdminOnly", "IsPersonal", "IsOptional", "IsDeleted")
                SELECT '{{Plugin}}', now(), 'migration:0033', false, false, false, false
                WHERE NOT EXISTS (SELECT 1 FROM prform."Plugins" WHERE "Description" = '{{Plugin}}' AND NOT "IsDeleted");

                INSERT INTO prform."PluginConfigurations" ("PluginId", "Options")
                SELECT p."Id", '[{}]'
                FROM prform."Plugins" p
                WHERE p."Description" = '{{Plugin}}' AND NOT p."IsDeleted"
                  AND NOT EXISTS (SELECT 1 FROM prform."PluginConfigurations" c WHERE c."PluginId" = p."Id");

                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(
                        COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb), '{0}',
                        '{{Quote(Options)}}'::jsonb || COALESCE(COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb) -> 0, '{}'::jsonb),
                        true)::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{Plugin}}' AND NOT p."IsDeleted";

                UPDATE prform."Plugins"
                SET "FieldSettings" = ('{{Quote(FieldSettings)}}'::jsonb || COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb))::text,
                    "UpdatedAt" = now()
                WHERE "Description" = '{{Plugin}}' AND NOT "IsDeleted";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DELETE FROM prform."PluginConfigurations" c USING prform."Plugins" p
                WHERE c."PluginId" = p."Id" AND p."Description" = '{{Plugin}}';
                DELETE FROM prform."Plugins" WHERE "Description" = '{{Plugin}}';
                """);
        }

        private static string Quote(string json) => json.Replace("'", "''");
    }
}
