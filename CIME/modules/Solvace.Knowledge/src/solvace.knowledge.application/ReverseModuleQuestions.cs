using System.Text.RegularExpressions;
using solvace.knowledge.domain.Entities;

namespace solvace.knowledge.application;

/// <summary>
/// Perguntas do "Pergunte" sobre um módulo (0054): pela seção sugerida/respondida ou pelo nome/apelido/sigla no texto.
/// 0097: as abertas e sem resposta (<see cref="ArchitectureQuestion.IsGap"/>) contam para sugerir a visão prática sob demanda.
/// </summary>
public static partial class ReverseModuleQuestions
{
    /// <summary>Nomes do módulo que a pergunta pode citar (normalizados como palavras: sem acento/caixa/pontuação).</summary>
    public static List<string> Names(ArchitectureProject project, ReverseModule? module) =>
        new[] { project.DisplayName, project.Name }.Concat(module?.Aliases ?? []).Concat(project.Keywords.Where(k => k.Length is >= 2 and <= 12))
            .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => Words(n!)).Where(n => n.Length >= 2).Distinct().ToList();

    /// <summary>Texto da pergunta como palavras separadas por um espaço, com espaço nas pontas (para casar palavra inteira).</summary>
    public static string Padded(string text) => " " + Words(text) + " ";

    /// <summary>A pergunta é do módulo: apontada para ele ou cita um dos nomes como palavra inteira.</summary>
    public static bool IsAbout(ArchitectureQuestion question, string projectKey, IReadOnlyList<string> names, string? padded = null)
    {
        if (question.SuggestedProject == projectKey || question.AnsweredProject == projectKey) return true;
        padded ??= Padded(question.Text);
        return names.Any(n => padded.Contains(" " + n + " ", StringComparison.Ordinal));
    }

    /// <summary>0097: aberta e sem resposta (not-found/partial) — a fila que a visão prática responde.</summary>
    public static bool IsOpenGap(ArchitectureQuestion question) => question.Status == ArchitectureQuestionStatus.Open && question.IsGap;

    private static string Words(string value) => NonWord().Replace(ArchitectureSearch.Normalize(value), " ").Trim();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWord();
}
