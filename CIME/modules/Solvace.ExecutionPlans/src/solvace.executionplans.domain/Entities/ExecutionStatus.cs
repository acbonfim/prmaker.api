namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Status do plano e das etapas (gravados como texto). Os valores são compartilhados com o
/// frontend (ExecutionPlanComponent) e com a skill (prmake-plan.sh) — mantenha em sincronia.
/// </summary>
public static class ExecutionStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    /// <summary>Etapa esperando algo externo (resposta, chamado, merge) — o plano segue andando (0024).</summary>
    public const string Waiting = "waiting";

    public static readonly IReadOnlySet<string> PlanStatuses =
        new HashSet<string> { Pending, Running, Paused, Completed, Failed, Cancelled };

    /// <summary>Etapa não pausa: quem pausa é o plano (a etapa em andamento aparece pausada na tela).</summary>
    public static readonly IReadOnlySet<string> StepStatuses =
        new HashSet<string> { Pending, Running, Waiting, Completed, Failed, Cancelled };

    /// <summary>Etapa que já terminou (bem ou mal) — não volta a ser cancelável pelo usuário.</summary>
    public static bool IsStepFinished(string status) =>
        status is Completed or Failed or Cancelled;
}

/// <summary>Tipos de registro de andamento (os "pedaços" que a skill manda).</summary>
public static class ExecutionLogKind
{
    public const string Info = "info";
    public const string Progress = "progress";
    public const string Finding = "finding";
    public const string Decision = "decision";
    public const string Warning = "warning";
    public const string Error = "error";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Info, Progress, Finding, Decision, Warning, Error };
}

/// <summary>Tipos de arquivo (viram os botões do rodapé e as pastas do .zip).</summary>
public static class ExecutionArtifactKind
{
    public const string Script = "script";
    public const string Analysis = "analysis";
    public const string Data = "data";
    public const string Image = "image";
    public const string Attachment = "attachment";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Script, Analysis, Data, Image, Attachment };

    private static readonly HashSet<string> ScriptExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".sql", ".sh", ".py", ".ps1", ".cs", ".js", ".ts", ".bash" };

    private static readonly HashSet<string> DataExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".json", ".csv", ".txt", ".log", ".xml", ".tsv", ".yaml", ".yml" };

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg" };

    /// <summary>Tipo pelo nome do arquivo, quando quem envia não informa.</summary>
    public static string Infer(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (ScriptExtensions.Contains(ext)) return Script;
        if (ext.Equals(".md", StringComparison.OrdinalIgnoreCase)) return Analysis;
        if (ImageExtensions.Contains(ext)) return Image;
        if (DataExtensions.Contains(ext)) return Data;
        return Attachment;
    }
}

/// <summary>Fase do plano (0024): a análise gera o plano de correção.</summary>
public static class ExecutionPhase
{
    public const string Analysis = "analysis";
    public const string Correction = "correction";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Analysis, Correction };
}

/// <summary>Quem executa a etapa (0024).</summary>
public static class ExecutionExecutor
{
    public const string Claude = "claude";
    public const string User = "user";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Claude, User };
}

/// <summary>Tipo da etapa (0024) — muda o ícone e as regras (PR conclui pelo merge; chamado pela resolução).</summary>
public static class ExecutionStepKind
{
    public const string Task = "task";
    public const string Code = "code";
    public const string PullRequest = "pr";
    public const string Ticket = "ticket";
    public const string Question = "question";
    public const string Validation = "validation";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Task, Code, PullRequest, Ticket, Question, Validation };
}

/// <summary>Links anexados às etapas (0024): chamados (status manual), PRs (status do GitHub), documentos.</summary>
public static class ExecutionLinkKind
{
    public const string Ticket = "ticket";
    public const string PullRequest = "pr";
    public const string Document = "doc";
    public const string Other = "other";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Ticket, PullRequest, Document, Other };

    /// <summary>Tipo pela URL quando quem envia não informa.</summary>
    public static string Infer(string url)
    {
        var u = url.ToLowerInvariant();
        if (u.Contains("freshservice.com") || u.Contains("/tickets/")) return Ticket;
        if (u.Contains("github.com") && u.Contains("/pull/")) return PullRequest;
        return Other;
    }
}

public static class ExecutionLinkStatus
{
    // Chamado (marcado à mão pelo usuário)
    public const string Open = "open";
    public const string Resolved = "resolved";
    public const string Closed = "closed";
    // PR (espelha o GitHub)
    public const string Merged = "merged";

    public static readonly IReadOnlySet<string> TicketStatuses = new HashSet<string> { Open, Resolved, Closed };
    public static readonly IReadOnlySet<string> PullRequestStatuses = new HashSet<string> { Open, Merged, Closed };
}

public static class ExecutionQuestionStatus
{
    public const string Open = "open";
    public const string Answered = "answered";
    public const string Cancelled = "cancelled";
}
