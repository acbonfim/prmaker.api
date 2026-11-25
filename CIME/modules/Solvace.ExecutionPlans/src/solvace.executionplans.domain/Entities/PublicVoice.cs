using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Texto que vai para a discussion do DevOps no nome de quem trata o card (contexto da passagem, situação publicada): em
/// primeira pessoa, sem citar o PRMake nem as funções dele ("Já resolvi", "nesta etapa", "avise no plano", Bancada...) e
/// sem falar com "você". O motivo de espera da etapa é texto da tela do PRMake e só entra no resumo quando passa aqui.
/// </summary>
public static partial class PublicVoice
{
    /// <summary>Termos do PRMake ou de instrução de tela encontrados no texto (vazio = pode ir para a discussion).</summary>
    public static IReadOnlyList<string> Problems(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var plain = Fold(text);
        return Forbidden().Matches(plain).Select(m => m.Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsClean(string? text) => Problems(text).Count == 0;

    /// <summary>Recusa o texto com a lista do que trocar (a skill reescreve e grava de novo).</summary>
    public static void Ensure(string? text, string what)
    {
        var problems = Problems(text);
        if (problems.Count == 0) return;
        throw new DomainException(
            $"{what} vai para a discussion do DevOps no nome de quem trata o card: escreva em primeira pessoa, sem citar o PRMake " +
            $"nem as funções dele e sem falar com \"você\" (trocar: {string.Join(", ", problems.Select(p => $"\"{p}\""))}). " +
            "Diga o que já fiz, o que falta e de quem (eu, o chamado, outra área) e quando volto a conferir — ex.: " +
            "\"Depois das 04:30 (Brasília) de 26/11 vou conferir a geração do dia para confirmar se a solução corrigiu o problema.\"");
    }

    // Minúsculas sem acento: "Já resolvi" e "ja resolvi" caem no mesmo termo.
    private static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    [GeneratedRegex(@"\b(prmake|ja resolvi|anexar chamado|nesta etapa|na etapa|(conclua|concluir|concluo) a etapa|o que a etapa pede|" +
                    @"(no|pelo|do) plano|plano de (correcao|implementacao|execucao)|bancada|sub-?tarefas?|item de trabalho|" +
                    @"clique|clicar|voce|quem trata o card)\b")]
    private static partial Regex Forbidden();
}
