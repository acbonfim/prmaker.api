using System.Text.RegularExpressions;
using solvace.knowledge.domain.Entities;

namespace solvace.knowledge.domain.Filtering;

/// <summary>Bloco de uma seção grande: posição/tamanho no texto (UTF-16) e o cabeçalho que o abre (null = antes do primeiro).</summary>
public sealed record SectionBlock(int Index, int Start, int Length, string? Heading);

/// <summary>Troca de um bloco: o texto original (conferido na posição) pelo proposto.</summary>
public sealed record SectionBlockEdit(int Start, string Original, string Proposed);

/// <summary>
/// Chat de melhoria em seção grande: a seção inteira (documentos da engenharia reversa de até milhões de caracteres) não cabe
/// no prompt nem na resposta da IA. A seção é dividida em blocos — um por cabeçalho (#..####, fora de bloco de código);
/// bloco maior que <see cref="MaxBlockChars"/> é cortado numa linha em branco — a IA recebe só os blocos do assunto e
/// devolve só os que mudou, e a troca é feita no servidor sobre o texto atual.
/// </summary>
public static partial class SectionBlocks
{
    public const int MaxBlockChars = 12_000;

    public static List<SectionBlock> Split(string? content, int maxBlock = MaxBlockChars)
    {
        var blocks = new List<SectionBlock>();
        if (string.IsNullOrEmpty(content)) return blocks;
        int start = 0, index = 0;
        string? heading = null, current = null;
        var fence = false;
        var hasText = false;

        void Flush(int end)
        {
            if (end > start && hasText) blocks.Add(new SectionBlock(blocks.Count, start, end - start, current));
            else if (end > start && blocks.Count > 0)
            {
                // só linhas em branco: ficam com o bloco anterior (a posição continua contígua)
                var last = blocks[^1];
                blocks[^1] = last with { Length = last.Length + end - start };
            }
            else if (end > start) blocks.Add(new SectionBlock(blocks.Count, start, end - start, current));
            start = end;
            hasText = false;
        }

        while (index < content.Length)
        {
            var newline = content.IndexOf('\n', index);
            var next = newline < 0 ? content.Length : newline + 1;
            var line = content[index..(newline < 0 ? content.Length : newline)].TrimEnd('\r');
            var trimmed = line.TrimStart();
            var isFence = trimmed.StartsWith("```", StringComparison.Ordinal);
            if (!fence && !isFence && HeadingLine().Match(line) is { Success: true } h)
            {
                if (index > start) Flush(index);
                heading = h.Groups[1].Value.Trim();
                current = heading;
            }
            else if (!fence && !isFence && trimmed.Length == 0 && index - start >= maxBlock)
            {
                Flush(index);
                current = heading is null ? null : heading + " (continuação)";
            }
            if (trimmed.Length > 0) hasText = true;
            if (isFence && (fence || trimmed.IndexOf("```", 3, StringComparison.Ordinal) < 0)) fence = !fence;
            index = next;
        }
        Flush(content.Length);
        return blocks;
    }

    public static string Text(string content, SectionBlock block) => content.Substring(block.Start, block.Length);

    /// <summary>
    /// Aplica as trocas sobre o texto atual. Cada original precisa estar exatamente na posição (senão a seção mudou e a
    /// proposta não vale mais). O proposto mantém o fim de linha do original, para o próximo cabeçalho seguir em linha própria.
    /// </summary>
    public static string Apply(string content, IEnumerable<SectionBlockEdit> edits)
    {
        var ordered = edits.OrderByDescending(e => e.Start).ToList();
        if (ordered.Count == 0) throw new DomainException("Nenhum bloco para aplicar.");
        var end = int.MaxValue;
        foreach (var edit in ordered)
        {
            if (edit.Start < 0 || edit.Start + edit.Original.Length > content.Length
                || string.CompareOrdinal(content, edit.Start, edit.Original, 0, edit.Original.Length) != 0)
                throw new DomainException("A seção mudou desde a proposta — peça a proposta de novo.");
            if (edit.Start + edit.Original.Length > end)
                throw new DomainException("Blocos sobrepostos na proposta.");
            end = edit.Start;
        }
        var text = content;
        foreach (var edit in ordered)
        {
            var tail = edit.Original[edit.Original.TrimEnd().Length..];
            var proposed = edit.Proposed.Replace("\r\n", "\n").Trim('\n').TrimEnd() + (tail.Length > 0 ? tail : string.Empty);
            text = string.Concat(text.AsSpan(0, edit.Start), proposed, text.AsSpan(edit.Start + edit.Original.Length));
        }
        return text;
    }

    [GeneratedRegex(@"^#{1,4}\s+(.+?)\s*$")]
    private static partial Regex HeadingLine();
}
