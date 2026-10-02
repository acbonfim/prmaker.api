using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace solvace.prform.infra.Migrations
{
    /// <summary>
    /// Consumo de IA por ação (0042): tabela <c>AiUsageRecords</c> e a tabela de preços (US$ por milhão de tokens) no
    /// "AI Configurations" → <c>AiModelPricesUsdPerMillion</c>, só se a chave não existir (valor editado pelo admin fica).
    /// O modelo casa pelo prefixo mais longo; modelo fora da tabela grava só os tokens.
    /// </summary>
    public partial class AddAiUsageRecords : Migration
    {
        private const string AIPlugin = "AI Configurations";

        private const string Prices = """
{"claude-haiku-4-5": {"input": 1, "output": 5}, "claude-3-5-haiku": {"input": 0.8, "output": 4}, "claude-3-haiku": {"input": 0.25, "output": 1.25}, "claude-sonnet-5": {"input": 2, "output": 10}, "claude-sonnet-4": {"input": 3, "output": 15}, "claude-3-7-sonnet": {"input": 3, "output": 15}, "claude-opus-4": {"input": 15, "output": 75}, "claude-opus-4-5": {"input": 5, "output": 25}, "claude-opus-4-6": {"input": 5, "output": 25}, "claude-opus-4-7": {"input": 5, "output": 25}, "claude-opus-4-8": {"input": 5, "output": 25}, "claude-opus-5": {"input": 5, "output": 25}, "claude-opus-5-5": {"input": 4, "output": 20}, "claude-fable-5": {"input": 10, "output": 50}, "gemini-2.5-flash": {"input": 0.3, "output": 2.5}, "gemini-2.5-pro": {"input": 1.25, "output": 10}, "gemini-2.0-flash": {"input": 0.1, "output": 0.4}, "gpt-4o-mini": {"input": 0.15, "output": 0.6}, "gpt-4o": {"input": 2.5, "output": 10}}
""";
        private const string FieldSettings = """
{"AiModelPricesUsdPerMillion": {"Label": "Preços da IA (US$ por milhão de tokens, JSON por modelo)", "Hidden": true, "Help": "Usado só para estimar o custo de cada ação (Meu consumo de IA). O modelo casa pelo prefixo mais longo."}}
""";
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiUsageRecords",
                schema: "prform",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserExternalId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Route = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(14,6)", precision: 14, scale: 6, nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_CreatedAt",
                schema: "prform",
                table: "AiUsageRecords",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_UserExternalId_CreatedAt",
                schema: "prform",
                table: "AiUsageRecords",
                columns: new[] { "UserExternalId", "CreatedAt" });

            // A chave guarda o JSON como texto (Options é um dicionário de strings).
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(
                        COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb), '{0}',
                        jsonb_build_object('AiModelPricesUsdPerMillion', '{{Prices}}'::jsonb::text)
                            || COALESCE(COALESCE(NULLIF(c."Options", '')::jsonb, '[{}]'::jsonb) -> 0, '{}'::jsonb),
                        true)::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{AIPlugin}}' AND NOT p."IsDeleted";

                UPDATE prform."Plugins"
                SET "FieldSettings" = ('{{FieldSettings}}'::jsonb || COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb))::text,
                    "UpdatedAt" = now()
                WHERE "Description" = '{{AIPlugin}}' AND NOT "IsDeleted";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                UPDATE prform."PluginConfigurations" c
                SET "Options" = jsonb_set(c."Options"::jsonb, '{0}', (c."Options"::jsonb -> 0) - 'AiModelPricesUsdPerMillion')::text
                FROM prform."Plugins" p
                WHERE p."Id" = c."PluginId" AND p."Description" = '{{AIPlugin}}' AND NULLIF(c."Options", '') IS NOT NULL;

                UPDATE prform."Plugins"
                SET "FieldSettings" = (COALESCE(NULLIF("FieldSettings", '')::jsonb, '{}'::jsonb) - 'AiModelPricesUsdPerMillion')::text
                WHERE "Description" = '{{AIPlugin}}';
                """);

            migrationBuilder.DropTable(
                name: "AiUsageRecords",
                schema: "prform");
        }
    }
}
