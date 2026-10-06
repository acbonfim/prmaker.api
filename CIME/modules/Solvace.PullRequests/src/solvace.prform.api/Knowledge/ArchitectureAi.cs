using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using solvace.ai.application.Contract;
using solvace.knowledge.domain.Responses;

namespace solvace.prform.Knowledge;

/// <summary>A IA não está configurada (plugin AI Configurations) — a tela mostra o motivo (HTTP 409).</summary>
public class ArchitectureAiUnavailableException(string reason) : Exception(reason);

/// <summary>Peças comuns dos serviços de IA da Base Solvace (0038): chamada com erro tratado, textos cortados e relações em português.</summary>
public static partial class ArchitectureAi
{
    public static async Task<(string Content, string? Provider, string? Model)> GenerateAsync(IAIService ai, string prompt, CancellationToken cancellationToken)
    {
        var result = await ai.GenerateContentAsync(prompt, cancellationToken);
        if (result is null || !string.IsNullOrWhiteSpace(result.Error) || string.IsNullOrWhiteSpace(result.Content))
            throw new InvalidOperationException(result?.Error ?? "O provedor de IA não respondeu.");
        return (result.Content, result.Provider, result.Model);
    }

    public static string Cut(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + "\n…(cortado)";
    }

    /// <summary>HTML do DevOps (repro steps, comentários) em texto.</summary>
    public static string PlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = Breaks().Replace(html, "\n");
        text = Tags().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = Spaces().Replace(text, " ");
        return BlankLines().Replace(text, "\n\n").Trim();
    }

    /// <summary>Relação em português, para a IA escrever para leigos.</summary>
    public static string RelationPhrase(string kind) => kind switch
    {
        "event" => "avisa por evento (mensagem publicada que outros escutam)",
        "queue" => "manda tarefas por fila (processadas em segundo plano)",
        "database" => "usa os mesmos dados (banco compartilhado)",
        "http" => "chama diretamente (API)",
        "package" => "usa peças de código (pacote)",
        "external" => "usa o serviço externo",
        "frontend" => "é a tela de",
        "cache" => "guarda/lê dados em cache (Redis)",
        "storage" => "troca arquivos (armazenamento, ex.: S3)",
        "job" => "é acionado por rotina agendada (job)",
        "trigger" => "é afetado por gatilho do banco (trigger)",
        _ => "se relaciona com"
    };

    public static void AppendRelations(StringBuilder sb, ArchitectureProjectResponse project, IReadOnlyDictionary<string, string> names)
    {
        string Name(string key) => names.TryGetValue(key, out var n) ? $"{n} ({key})" : key;
        if (project.Relations.Count > 0)
        {
            sb.AppendLine("Depende de / conversa com:");
            foreach (var r in project.Relations.Take(60))
                sb.AppendLine($"- {RelationPhrase(r.Kind)} → {Name(r.Target)}{(string.IsNullOrWhiteSpace(r.Detail) ? "" : $": {r.Detail}")}");
        }
        if (project.UsedBy.Count > 0)
        {
            sb.AppendLine("Quem usa este sistema:");
            foreach (var r in project.UsedBy.Take(60))
                sb.AppendLine($"- {Name(r.Source)} {RelationPhrase(r.Kind)} este sistema{(string.IsNullOrWhiteSpace(r.Detail) ? "" : $": {r.Detail}")}");
        }
    }

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/h\d)\s*/?>", RegexOptions.IgnoreCase)] private static partial Regex Breaks();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex(@"[ \t ]+")] private static partial Regex Spaces();
    [GeneratedRegex(@"\n\s*\n\s*(\n\s*)+")] private static partial Regex BlankLines();
}
