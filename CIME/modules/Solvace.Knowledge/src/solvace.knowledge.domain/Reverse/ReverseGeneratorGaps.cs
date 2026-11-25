using System.Globalization;
using System.Text;

namespace solvace.knowledge.domain.Reverse;

/// <summary>
/// 0097: GAP "do gerador" — a lacuna fala do processo de geração (pacote, área, subagente, Figma, inventário…) e não do
/// sistema. Não entra no índice (a publicação ignora; a migração apagou os existentes), a checagem avisa e a skill os tira
/// no <c>juntar</c>. LISTA ÚNICA: a skill tem a mesma em <c>skills/engenharia-reversa/scripts/re_gaps.py</c> (teste de
/// paridade) — mude as duas juntas.
/// <para>Comparação: título + corpo do item normalizados por <see cref="Normalize"/> (sem acento, minúsculas, tudo que não
/// é letra/dígito vira um espaço) e procurados como trecho (substring).</para>
/// </summary>
public static class ReverseGeneratorGaps
{
    /// <summary>Trechos que, em qualquer GAP, dizem que a lacuna é do gerador.</summary>
    public static readonly IReadOnlyList<string> Phrases =
    [
        "fora do pacote",
        "nao esta no pacote",
        "pacote nao traz",
        "pacote nao inclui",
        "sem figma",
        "figma nao",
        "nao lido nesta area",
        "fora desta area",
        "orcamento da area",
        "subagente",
        "inventario nao",
        "retrato nao"
    ];

    /// <summary>Trechos que só contam quando o GAP não cita código (nenhuma evidência <c>arquivo:linha</c>/banco/AWS).</summary>
    public static readonly IReadOnlyList<string> PhrasesWithoutEvidence =
    [
        "nao foi possivel ler"
    ];

    /// <summary>O GAP fala do processo de geração (e não do sistema)?</summary>
    public static bool IsGeneratorGap(ReverseItem item) =>
        item.Kind == "GAP" && !item.Removed && Matches(item.Title + "\n" + item.Body, item.Evidence.Count > 0);

    /// <summary>O texto (título + corpo) casa com algum trecho da lista; <paramref name="citesCode"/> desliga os que exigem "sem citar código".</summary>
    public static bool Matches(string text, bool citesCode)
    {
        var n = " " + Normalize(text) + " ";
        return Phrases.Any(p => n.Contains(p, StringComparison.Ordinal))
               || (!citesCode && PhrasesWithoutEvidence.Any(p => n.Contains(p, StringComparison.Ordinal)));
    }

    /// <summary>Sem acento, minúsculas, e qualquer sequência que não seja letra/dígito vira um espaço (igual ao <c>re_gaps.py</c>).</summary>
    public static string Normalize(string? value)
    {
        var decomposed = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var space = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = char.ToLowerInvariant(ch);
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(c);
                space = false;
            }
            else if (!space && sb.Length > 0)
            {
                sb.Append(' ');
                space = true;
            }
        }
        return sb.ToString().TrimEnd();
    }
}
