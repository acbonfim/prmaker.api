using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace solvace.knowledge.infra.Migrations
{
    /// <summary>
    /// 0097: apaga do índice os GAP "do gerador" já publicados — a lacuna fala do processo de geração (pacote, área,
    /// subagente, Figma, inventário…) e não do sistema. Mesma regra de <c>ReverseGeneratorGaps</c> (lista copiada aqui de
    /// propósito: a migração fica fiel ao que era a regra quando rodou): título + corpo sem acento, minúsculas, o que não é
    /// letra/dígito vira espaço, e a frase procurada como trecho; "não foi possível ler" só quando o item não cita código.
    /// Só o índice muda (o texto publicado fica; a próxima publicação já ignora). Down não recria — republicar o documento
    /// recria o índice dele.
    /// </summary>
    public partial class ReverseGeneratorGaps0097 : Migration
    {
        private static readonly string[] Phrases =
        [
            "fora do pacote", "nao esta no pacote", "pacote nao traz", "pacote nao inclui", "sem figma", "figma nao",
            "nao lido nesta area", "fora desta area", "orcamento da area", "subagente", "inventario nao", "retrato nao"
        ];

        private static readonly string[] PhrasesWithoutEvidence = ["nao foi possivel ler"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            static string Any(IEnumerable<string> phrases) => string.Join(" OR ", phrases.Select(p => $"strpos(g.n, '{p}') > 0"));
            migrationBuilder.Sql($"""
                WITH g AS (
                    SELECT e."Id",
                           ' ' || btrim(regexp_replace(lower(translate(e."Title" || ' ' || e."Body",
                               'ÁÀÂÃÄÅáàâãäåÉÈÊËéèêëÍÌÎÏíìîïÓÒÔÕÖóòôõöÚÙÛÜúùûüÇçÑñÝýÿ',
                               'AAAAAAaaaaaaEEEEeeeeIIIIiiiiOOOOOoooooUUUUuuuuCcNnYyy')), '[^a-z0-9]+', ' ', 'g')) || ' ' AS n,
                           CASE WHEN jsonb_typeof(e."Evidence") = 'array' THEN jsonb_array_length(e."Evidence") ELSE 0 END AS evidence
                    FROM knowledge."ReverseIndexEntries" e
                    WHERE e."Kind" = 'GAP' AND NOT e."Removed"
                )
                DELETE FROM knowledge."ReverseIndexEntries" d
                USING g
                WHERE d."Id" = g."Id"
                  AND (({Any(Phrases)}) OR (g.evidence = 0 AND ({Any(PhrasesWithoutEvidence)})));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nada a desfazer no esquema: republicar o documento recria o índice dele.
        }
    }
}
