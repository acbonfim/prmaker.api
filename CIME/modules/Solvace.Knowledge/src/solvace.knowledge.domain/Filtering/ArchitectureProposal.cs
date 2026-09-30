using System.Text.RegularExpressions;

namespace solvace.knowledge.domain.Filtering;

/// <summary>
/// Separa a resposta do especialista (chat de melhoria, 0033) da seção proposta, que vem entre as linhas
/// <c>&lt;&lt;&lt;SECAO</c> e <c>SECAO&gt;&gt;&gt;</c> — marcadores próprios porque a seção pode conter blocos ```mermaid```.
/// </summary>
public static partial class ArchitectureProposal
{
    public const string Open = "<<<SECAO";
    public const string Close = "SECAO>>>";

    public static (string Reply, string? Suggestion) Split(string content)
    {
        var match = Proposal().Match(content ?? string.Empty);
        if (!match.Success) return ((content ?? string.Empty).Trim(), null);
        var suggestion = match.Groups[1].Value.Trim();
        var reply = (content![..match.Index] + content[(match.Index + match.Length)..]).Trim();
        return (reply.Length == 0 ? "Proposta de nova versão abaixo." : reply, suggestion.Length == 0 ? null : suggestion);
    }

    [GeneratedRegex(@"<<<SECAO[ \t]*\r?\n(.*?)\r?\n[ \t]*SECAO>>>", RegexOptions.Singleline)]
    private static partial Regex Proposal();
}
