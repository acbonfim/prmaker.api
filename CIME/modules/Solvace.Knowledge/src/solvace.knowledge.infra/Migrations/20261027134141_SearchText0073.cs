using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <summary>
    /// 0073: a busca da engenharia reversa deixa de montar o índice inteiro na memória da API e passa a escolher os candidatos no
    /// banco. Colunas geradas (sem acento/caixa — <c>translate</c> + <c>lower</c>, imutáveis, sem depender de extensão) para o
    /// <c>LIKE '%termo%'</c>; GIN <c>jsonb_path_ops</c> nas listas que o "quem cita" e o impacto consultam. Sem índice de trigramas
    /// (<c>pg_trgm</c>) de propósito: medido com 60 mil itens, para as palavras comuns de uma consulta (42% dos itens têm "regra") a
    /// varredura sequencial é mais rápida que o índice (0,2–0,5 s contra 0,7–1,6 s) e o índice custa ~75 MB.
    /// </summary>
    public partial class SearchText0073 : Migration
    {
        private const string Accents = "áàâãäåéèêëíìîïóòôõöúùûüçñýÿ";
        private const string Plain = "aaaaaaeeeeiiiiooooouuuucnyy";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
CREATE OR REPLACE FUNCTION knowledge.search_normalize(t text) RETURNS text LANGUAGE sql IMMUTABLE PARALLEL SAFE AS
$$ SELECT translate(lower(t), '{Accents}', '{Plain}') $$;");

            migrationBuilder.Sql(@"
ALTER TABLE knowledge.""ReverseIndexEntries"" ADD COLUMN ""SearchText"" text GENERATED ALWAYS AS (
  knowledge.search_normalize(""ItemId"" || ' ' || ""Title"" || ' ' || ""Tags""::text || ' ' || ""Synonyms""::text || ' ' || ""Tables""::text || ' ' || ""Modules""::text || ' ' || ""Body"")
) STORED;");

            // palavras do título e das tags/sinônimos/módulos, só letras e dígitos, com espaço nas pontas — para os nomes do glossário (frase inteira)
            migrationBuilder.Sql(@"
ALTER TABLE knowledge.""ReverseIndexEntries"" ADD COLUMN ""HeadWords"" text GENERATED ALWAYS AS (
  ' ' || regexp_replace(knowledge.search_normalize(""Title""), '[^a-z0-9]+', ' ', 'g') || ' '
      || regexp_replace(knowledge.search_normalize(""Tags""::text || ' ' || ""Synonyms""::text || ' ' || ""Modules""::text), '[^a-z0-9]+', ' ', 'g') || ' '
) STORED;");

            // partes pequenas já normalizadas: a pontuação aproximada dos candidatos compara título/tags/tabelas sem recalcular por linha e por termo
            migrationBuilder.Sql(@"ALTER TABLE knowledge.""ReverseIndexEntries"" ADD COLUMN ""SearchTitle"" text GENERATED ALWAYS AS (knowledge.search_normalize(""ItemId"" || ' ' || ""Title"")) STORED;");
            migrationBuilder.Sql(@"ALTER TABLE knowledge.""ReverseIndexEntries"" ADD COLUMN ""SearchTags"" text GENERATED ALWAYS AS (knowledge.search_normalize(""Tags""::text || ' ' || ""Synonyms""::text || ' ' || ""Modules""::text)) STORED;");
            migrationBuilder.Sql(@"ALTER TABLE knowledge.""ReverseIndexEntries"" ADD COLUMN ""SearchTables"" text GENERATED ALWAYS AS (knowledge.search_normalize(""Tables""::text)) STORED;");

            migrationBuilder.Sql(@"CREATE INDEX ""IX_ReverseIndexEntries_Refs"" ON knowledge.""ReverseIndexEntries"" USING gin (""Refs"" jsonb_path_ops);");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_ReverseIndexEntries_Tables"" ON knowledge.""ReverseIndexEntries"" USING gin (""Tables"" jsonb_path_ops);");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_ReverseIndexEntries_Modules"" ON knowledge.""ReverseIndexEntries"" USING gin (""Modules"" jsonb_path_ops);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS knowledge.""IX_ReverseIndexEntries_Modules"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS knowledge.""IX_ReverseIndexEntries_Tables"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS knowledge.""IX_ReverseIndexEntries_Refs"";");
            migrationBuilder.Sql(@"ALTER TABLE knowledge.""ReverseIndexEntries"" DROP COLUMN IF EXISTS ""SearchTables"";");
            migrationBuilder.Sql(@"ALTER TABLE knowledge.""ReverseIndexEntries"" DROP COLUMN IF EXISTS ""SearchTags"";");
            migrationBuilder.Sql(@"ALTER TABLE knowledge.""ReverseIndexEntries"" DROP COLUMN IF EXISTS ""SearchTitle"";");
            migrationBuilder.Sql(@"ALTER TABLE knowledge.""ReverseIndexEntries"" DROP COLUMN IF EXISTS ""HeadWords"";");
            migrationBuilder.Sql(@"ALTER TABLE knowledge.""ReverseIndexEntries"" DROP COLUMN IF EXISTS ""SearchText"";");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS knowledge.search_normalize(text);");
        }
    }
}
