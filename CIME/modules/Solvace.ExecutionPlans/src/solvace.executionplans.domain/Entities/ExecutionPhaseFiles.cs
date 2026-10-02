using System.Text;
using System.Text.RegularExpressions;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0050: cada arquivo no plano da sua fase. Análise = consultas somente leitura que levaram ao problema, análise e
/// dados; correção = script que altera dados (com rollback), validação e texto do chamado — rodar o script é execução.
/// Vale para o que o Claude grava (skill/MCP); o usuário anexa o que quiser pela tela.
/// </summary>
public static partial class ExecutionPhaseFiles
{
    public const string ChangesDataInAnalysis =
        "Script que altera dados vai para o plano de correção (prmake_file com phase \"correction\" e key = a etapa do chamado); " +
        "no plano de análise ficam só as consultas somente leitura que levaram ao problema (00_consulta-<assunto>.sql).";

    public const string TicketInAnalysis =
        "O texto do chamado vai para o plano de correção, na etapa do chamado (prmake_file com kind \"ticket\", phase \"correction\" " +
        "e key = a etapa) — na análise, descreva o chamado no solucoes.md e no resumo do checkpoint de propor-solucoes.";

    /// <summary>Motivo da recusa, ou null quando o arquivo pode ficar neste plano.</summary>
    public static string? Reject(string phase, string kind, string fileName, byte[] data)
    {
        if (phase != ExecutionPhase.Analysis) return null;
        if (kind == ExecutionArtifactKind.Ticket) return TicketInAnalysis;
        if (kind == ExecutionArtifactKind.Script
            && Path.GetExtension(fileName).Equals(".sql", StringComparison.OrdinalIgnoreCase)
            && ChangesData(Encoding.UTF8.GetString(data)))
            return ChangesDataInAnalysis;
        return null;
    }

    /// <summary>SQL com comando que altera dados ou estrutura, fora de comentários e de textos entre aspas.</summary>
    public static bool ChangesData(string sql)
    {
        var code = Comments().Replace(sql, " ");
        code = Literals().Replace(code, "''");
        return Changing().IsMatch(code);
    }

    [GeneratedRegex(@"--[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"'(?:[^']|'')*'")]
    private static partial Regex Literals();

    [GeneratedRegex(@"\b(UPDATE|INSERT|DELETE|MERGE|TRUNCATE|ALTER|DROP|CREATE|EXEC|EXECUTE|CALL|GRANT|REVOKE|RENAME)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Changing();
}
