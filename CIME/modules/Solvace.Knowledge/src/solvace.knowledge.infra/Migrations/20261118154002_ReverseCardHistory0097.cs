using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <summary>
    /// 0097: histórico do card — <c>Query</c> (título + repro normalizados) e <c>ReadRefs</c> (refs realmente lidos). Backfill
    /// do <c>Query</c> dos cards já registrados com o título que o banco tem: o "título curto" do plano de análise
    /// (<c>execution."ExecutionPlans"</c>, "Análise do bug 77697: &lt;título&gt;") e, na falta, o título do card na promoção
    /// em lote (<c>prform."PromotionItems"</c>). Cada fonte só se a tabela existir (banco novo: as migrações de outro
    /// contexto podem vir depois). Sem acento/caixa como o código grava (a repro não existe no banco: fica para a próxima
    /// análise do card).
    /// </summary>
    public partial class ReverseCardHistory0097 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Query",
                schema: "knowledge",
                table: "ReverseCardContexts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReadRefs",
                schema: "knowledge",
                table: "ReverseCardContexts",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.Sql($$"""
                DO $$
                BEGIN
                  IF to_regclass('execution."ExecutionPlans"') IS NOT NULL THEN
                    UPDATE knowledge."ReverseCardContexts" c
                    SET "Query" = left(t.q, 1000)
                    FROM (
                      SELECT DISTINCT ON (p."CardNumber") p."CardNumber",
                             btrim(regexp_replace(lower(translate(
                                 regexp_replace(p."Title", '^\s*an[aá]lise[^0-9]*[0-9]+\s*[:—–-]?\s*', '', 'i'),
                                 '{{Accents}}', '{{Plain}}')), '\s+', ' ', 'g')) AS q
                      FROM execution."ExecutionPlans" p
                      WHERE p."Phase" = 'analysis'
                      ORDER BY p."CardNumber", p."CreatedAt" DESC
                    ) t
                    WHERE c."CardNumber" = t."CardNumber" AND c."Query" IS NULL AND length(t.q) >= 3;
                  END IF;
                  IF to_regclass('prform."PromotionItems"') IS NOT NULL THEN
                    UPDATE knowledge."ReverseCardContexts" c
                    SET "Query" = left(t.q, 1000)
                    FROM (
                      SELECT DISTINCT ON (i."CardNumber") i."CardNumber",
                             btrim(regexp_replace(lower(translate(i."CardTitle", '{{Accents}}', '{{Plain}}')), '\s+', ' ', 'g')) AS q
                      FROM prform."PromotionItems" i
                      ORDER BY i."CardNumber", i."CreatedAt" DESC
                    ) t
                    WHERE c."CardNumber" = t."CardNumber" AND c."Query" IS NULL AND length(t.q) >= 3;
                  END IF;
                END $$;
                """);
        }

        private const string Accents = "ÁÀÂÃÄÅáàâãäåÉÈÊËéèêëÍÌÎÏíìîïÓÒÔÕÖóòôõöÚÙÛÜúùûüÇçÑñÝýÿ";
        private const string Plain = "AAAAAAaaaaaaEEEEeeeeIIIIiiiiOOOOOoooooUUUUuuuuCcNnYyy";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Query",
                schema: "knowledge",
                table: "ReverseCardContexts");

            migrationBuilder.DropColumn(
                name: "ReadRefs",
                schema: "knowledge",
                table: "ReverseCardContexts");
        }
    }
}
