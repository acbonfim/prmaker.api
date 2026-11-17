using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;

namespace solvace.azure.application.Services;

/// <summary>
/// Card 78525: especificação da User Story (refinamento) — seções configuradas (seção → campo do work item), o texto
/// separado pelas linhas de título das seções e o markdown convertido para o HTML que o DevOps mostra nos campos.
/// </summary>
public static partial class UserStorySpec
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>JSON {"Seção": "Campo"} na ordem; inválido/vazio → o padrão.</summary>
    public static IReadOnlyList<(string Section, string Field)> ParseMap(string? json, string fallbackJson)
    {
        foreach (var candidate in new[] { json, fallbackJson })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                var list = doc.RootElement.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()) && !string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => (p.Name.Trim(), p.Value.GetString()!.Trim()))
                    .ToList();
                if (list.Count > 0) return list;
            }
            catch (JsonException)
            {
                // configuração inválida → o padrão
            }
        }
        return [];
    }

    /// <summary>"1. **High Level Business Specification (PM)**:" → "high level business specification pm".</summary>
    public static string Normalize(string text)
    {
        var t = text.Replace("**", string.Empty).Replace("__", string.Empty).Trim();
        t = LeadingMarks().Replace(t, string.Empty).TrimEnd(':', ' ', '#');
        return Spaces().Replace(NonWord().Replace(t.ToLowerInvariant(), " "), " ").Trim();
    }

    /// <summary>Sem o que vem entre parênteses ("Technical Specification (Tech Lead / Architect)" → "technical specification").</summary>
    private static string Base(string text) => Normalize(Parens().Replace(text, " "));

    /// <summary>A linha (título) é uma das seções? Igual inteira ou sem os parênteses.</summary>
    public static string? MatchSection(string line, IReadOnlyList<(string Section, string Field)> map)
    {
        var raw = line.Trim();
        if (raw.Length == 0 || raw.Length > 160) return null;
        var n = Normalize(raw);
        var b = Base(raw);
        if (b.Length == 0) return null;
        foreach (var (section, _) in map)
            if (Normalize(section) == n || Base(section) == b)
                return section;
        return null;
    }

    /// <summary>Separa o documento pelas linhas de título das seções. Texto antes da primeira seção é ignorado.</summary>
    public static Dictionary<string, string> Split(string document, IReadOnlyList<(string Section, string Field)> map)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? current = null;
        var buffer = new List<string>();
        void Flush()
        {
            if (current is null) return;
            var text = string.Join("\n", buffer).Trim();
            if (text.Length > 0) result[current] = text;
            buffer.Clear();
        }
        foreach (var line in document.Replace("\r\n", "\n").Split('\n'))
        {
            if (MatchSection(line, map) is { } section)
            {
                Flush();
                current = section;
                continue;
            }
            if (current is not null) buffer.Add(line);
        }
        Flush();
        return result;
    }

    public static string ToHtml(string markdown) => Markdown.ToHtml(markdown.Trim(), Pipeline).Trim();

    [GeneratedRegex(@"^(#{1,6}\s*)?(\d+\s*[.)-]\s*)?")]
    private static partial Regex LeadingMarks();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parens();
}
