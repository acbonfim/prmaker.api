namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0085: status do item de trabalho (gravado como texto). Compartilhado com o frontend (WorkItem) e com as skills
/// (executar/desfazer item) — mantenha em sincronia.
/// </summary>
public static class WorkItemStatus
{
    public const string Pending = "pending";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Waiting = "waiting";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Reverting = "reverting";
    public const string Reverted = "reverted";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
        { Pending, Queued, Running, Waiting, Completed, Failed, Cancelled, Reverting, Reverted };

    /// <summary>Item que não conta na verificação de duplicidade (foi cancelado ou desfeito).</summary>
    public static readonly IReadOnlySet<string> Discarded = new HashSet<string> { Cancelled, Reverted };

    /// <summary>Item fechado: não roda mais (só desfazer, no caso de concluído/cancelado/falho).</summary>
    public static bool IsClosed(string status) => status is Completed or Cancelled or Reverted;
}

/// <summary>0085: status da sub-tarefa do item.</summary>
public static class WorkTaskStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Waiting = "waiting";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string Cancelled = "cancelled";
    public const string Reverted = "reverted";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
        { Pending, Running, Waiting, Completed, Failed, Skipped, Cancelled, Reverted };

    /// <summary>Sub-tarefa que conta como feita no progresso.</summary>
    public static bool IsDone(string status) => status is Completed or Skipped;

    /// <summary>Sub-tarefa ainda aberta (cancelar o item cancela estas).</summary>
    public static bool IsOpen(string status) => status is Pending or Running or Waiting;
}

/// <summary>0085: ações sobre a sub-tarefa (REST <c>tasks/{n}/{action}</c> e MCP <c>prmake_work_task</c>).</summary>
public static class WorkTaskAction
{
    public const string Start = "start";
    public const string Complete = "complete";
    public const string Fail = "fail";
    public const string Wait = "wait";
    public const string Skip = "skip";
    public const string Revert = "revert";
    public const string Cancel = "cancel";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Start, Complete, Fail, Wait, Skip, Revert, Cancel };
}

/// <summary>0085: de onde veio o item.</summary>
public static class WorkItemOrigin
{
    public const string User = "user";
    public const string Specialist = "specialist";
    public const string Analysis = "analysis";
    public const string Correction = "correction";
    public const string Template = "template";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { User, Specialist, Analysis, Correction, Template };
}

/// <summary>
/// 0085: tipo do item de trabalho. Rodada 4: tipos próprios (configuráveis em <c>WorkItemTypes</c> do "Skills Configurations";
/// semeados: task, pull-request, review, analysis, fix, implementation, adjustment, closing, data, ticket, text) — o tipo do
/// card no DevOps (Bug/User Story) não é o tipo do item. <c>bug</c>/<c>user-story</c> ficam só nos itens antigos (exibição);
/// criação nova mapeia <c>bug</c> → <c>fix</c> e <c>user-story</c> → <c>implementation</c> (<see cref="MapLegacy"/>).
/// </summary>
public static partial class WorkItemType
{
    public const string Task = "task";
    public const string PullRequest = "pull-request";
    public const string Review = "review";
    public const string Analysis = "analysis";
    public const string Fix = "fix";
    public const string Implementation = "implementation";
    public const string Adjustment = "adjustment";
    public const string Closing = "closing";
    public const string Data = "data";
    public const string Ticket = "ticket";
    public const string Text = "text";

    /// <summary>Legado (itens antigos): seguiam o tipo do card.</summary>
    public const string Bug = "bug";
    public const string UserStory = "user-story";

    public const int MaxLength = 20;

    /// <summary>Tipos semeados (padrão quando <c>WorkItemTypes</c> não está configurado).</summary>
    public static readonly IReadOnlyList<string> Defaults =
        [Task, PullRequest, Review, Analysis, Fix, Implementation, Adjustment, Closing, Data, Ticket, Text];

    public static readonly IReadOnlySet<string> Legacy = new HashSet<string> { Bug, UserStory };

    /// <summary>Tipo do item da correção pelo tipo do card: User Story → implementation; o resto → fix.</summary>
    public static string CorrectionFor(bool userStory) => userStory ? Implementation : Fix;

    /// <summary>Criação nova: <c>bug</c> → <c>fix</c>, <c>user-story</c> (e apelidos) → <c>implementation</c>; o resto como veio.</summary>
    public static string? MapLegacy(string? type)
    {
        var t = type?.Trim().ToLowerInvariant().Replace(' ', '-');
        if (string.IsNullOrEmpty(t)) return null;
        return t switch
        {
            Bug => Fix,
            UserStory or "us" or "userstory" or "story" => Implementation,
            _ => t
        };
    }

    /// <summary>Normaliza (vazio → task; apelidos de US → user-story) e confere o formato (slug até 20). O que existe é
    /// conferido na aplicação contra o <c>WorkItemTypes</c>.</summary>
    public static string Normalize(string? type)
    {
        var t = (type ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '-');
        if (t.Length == 0) return Task;
        if (t is "us" or "userstory" or "story") t = UserStory;
        if (t.Length > MaxLength || !Slug().IsMatch(t))
            throw new DomainException($"Tipo de item inválido: '{type}' (letras minúsculas, números e '-', até {MaxLength}).");
        return t;
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial System.Text.RegularExpressions.Regex Slug();
}

/// <summary>
/// 0085: tipos conhecidos de sub-tarefa (o catálogo com rótulo/ícone/parâmetros é configurável em
/// <c>WorkItemTaskKinds</c>; o domínio aceita qualquer slug para não travar um tipo novo do catálogo).
/// </summary>
public static class WorkTaskKind
{
    public const string Branch = "branch";
    public const string CherryPick = "cherry-pick";
    public const string Code = "code";
    public const string PullRequest = "pr";
    public const string DevOpsState = "devops-state";
    public const string SqlScript = "sql-script";
    public const string Validation = "validation";
    public const string Comment = "comment";
    public const string Manual = "manual";
    /// <summary>r3: abrir chamado (fluxo de chamado do PRMake).</summary>
    public const string Ticket = "ticket";
    /// <summary>r3: avisar na Timeline/Teams.</summary>
    public const string Notify = "notify";
    /// <summary>r4: ação do PRMake no card (root cause, resumo, classificação, Remaining, estados).</summary>
    public const string DevOpsAction = "devops-action";
    /// <summary>r4: descrição + RCA pelo caminho da gerar-prmake, sem abrir PR.</summary>
    public const string PrText = "pr-text";
    /// <summary>r4: consulta somente leitura (resultado nos arquivos do plano).</summary>
    public const string SqlQuery = "sql-query";
    /// <summary>r4: revisar um PR (nunca aprova nem mescla).</summary>
    public const string PrReview = "pr-review";
    /// <summary>0089: resumo do que o card pede (US) ou reclama (Bug), lendo campos, discussion e evidências.</summary>
    public const string CardBrief = "card-brief";
    /// <summary>0089: arquivo do item com o racional (fica nos arquivos do card).</summary>
    public const string CardBriefFileName = "resumo-do-pedido.md";

    /// <summary>Tipos de sub-tarefa: levantamento/consulta sem alterar nada (impacto na base, ler o card, mapear o código).</summary>
    public const string Research = "research";
    /// <summary>Tipos de sub-tarefa: propor e decidir com a pessoa (perguntas da análise).</summary>
    public const string Decision = "decision";
    /// <summary>Tipos de sub-tarefa: tarefa do Claude que não se encaixa em outro tipo.</summary>
    public const string Task = "task";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>
        { Branch, CherryPick, Code, PullRequest, DevOpsState, SqlScript, Validation, Comment, Manual, Ticket, Notify,
          DevOpsAction, PrText, SqlQuery, PrReview, CardBrief, Research, Decision, Task };

    /// <summary>
    /// Tipo da sub-tarefa que espelha uma etapa do plano: o <c>taskKind</c> que a skill informou; sem ele, pelo tipo da
    /// etapa (code/pr/validation/ticket/question) e, na etapa <c>task</c>, pela chave e pelo título — nunca "Alterar
    /// código" por falta de opção (etapa <c>task</c> do Claude sem pista = <see cref="Task"/>).
    /// </summary>
    public static string FromStep(string stepKind, string executor, string? key = null, string? title = null, string? taskKind = null)
    {
        if (!string.IsNullOrWhiteSpace(taskKind)) return WorkItemTask.NormalizeKind(taskKind);
        switch (stepKind)
        {
            case ExecutionStepKind.Code: return Code;
            case ExecutionStepKind.PullRequest: return PullRequest;
            case ExecutionStepKind.Validation: return Validation;
            case ExecutionStepKind.Ticket: return Ticket;
            case ExecutionStepKind.Question: return Decision;
        }
        if (executor == ExecutionExecutor.User) return Manual;
        return FromKey(key) ?? FromTitle(title) ?? Task;
    }

    /// <summary>O tipo que a versão anterior dava à etapa (sub-tarefa ainda com ele = escolhida pelo servidor, não pela pessoa).</summary>
    public static string LegacyFromStep(string stepKind, string executor) => stepKind switch
    {
        ExecutionStepKind.Code => Code,
        ExecutionStepKind.PullRequest => PullRequest,
        ExecutionStepKind.Validation => Validation,
        ExecutionStepKind.Ticket or ExecutionStepKind.Question => Manual,
        _ => executor == ExecutionExecutor.User ? Manual : Code,
    };

    /// <summary>Chaves das etapas padrão das skills (analisar-bug/analisar-us).</summary>
    private static string? FromKey(string? key)
    {
        var k = (key ?? string.Empty).Trim().ToLowerInvariant();
        if (k.Length == 0) return null;
        switch (k)
        {
            case "identificar-card" or "coletar-dados" or "entender-us" or "consultar-base" or "mapear-codigo"
                or "investigar-codigo" or "causa-raiz" or "desenhar-solucao" or "montar-analise" or "impacto-na-base":
                return Research;
            case "consultar-ambiente": return SqlQuery;
            case "publicar": return Notify;
            case "propor-solucoes": return Decision;
            case "preparar-fechamento" or "fechar-card": return DevOpsAction;
        }
        if (k.StartsWith("preparar-script", StringComparison.Ordinal)) return SqlScript;
        if (k.StartsWith("pr-", StringComparison.Ordinal)) return PullRequest;
        if (k.StartsWith("implementar-", StringComparison.Ordinal) || k.StartsWith("corrigir-", StringComparison.Ordinal)) return Code;
        // Chamado na Bancada: conferir os dados depois do chamado (somente leitura) — antes da regra geral de "validar".
        if (k.StartsWith("validar-chamado", StringComparison.Ordinal) || k.StartsWith("validar-dados", StringComparison.Ordinal)) return SqlQuery;
        if (k.StartsWith("validar", StringComparison.Ordinal)) return Validation;
        return null;
    }

    /// <summary>Etapa com chave própria: pelo que o título diz (a primeira regra que casa).</summary>
    private static string? FromTitle(string? title)
    {
        var t = Fold(title);
        if (t.Length == 0) return null;
        if (Has(t, "cherry-pick", "cherry pick")) return CherryPick;
        if (Has(t, "criar a branch", "criar branch")) return Branch;
        if (Has(t, "revisar o pr", "revisar pr", "revisar os pr", "review")) return PrReview;
        if (Has(t, "prmake", " rca ", "texto do pr", "descricao do pr")) return PrText;
        if (Has(t, "abrir o pr", "abrir os pr", "abrir pr", "pull request")) return PullRequest;
        if (Has(t, "fechar o card", "fechar card", "fechamento", "mover o card", "mover card", "root cause", "devops",
                "ready for qa", "test in production", "classificar", "remaining")) return DevOpsAction;
        if (Has(t, "script")) return SqlScript;
        if (Has(t, "consultar o banco", "consultar dados", "consulta sql", "select ")) return SqlQuery;
        if (Has(t, "timeline", "avisar", "notificar", "teams")) return Notify;
        if (Has(t, "comentario", "comentar", "discussion")) return Comment;
        if (Has(t, "impacto", "consultar", " ler ", "identificar", "entender", "mapear", "levantar", "investigar",
                "confirmar", "analisar", "analise", "base solvace", "desenhar", "causa raiz", "reproduzir", "evidencia",
                "coletar", "conferir")) return Research;
        if (Has(t, "implementar", "alterar o codigo", "corrigir o codigo", "ajustar o codigo", "codigo")) return Code;
        if (Has(t, "validar", "testar", "build")) return Validation;
        return null;
    }

    private static bool Has(string text, params string[] terms) => terms.Any(text.Contains);

    /// <summary>Minúsculas, sem acento, pontuação vira espaço e espaço nas pontas (casa " ler " e " rca " como palavra).</summary>
    private static string Fold(string? text)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return string.Empty;
        var decomposed = t.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length + 2).Append(' ');
        foreach (var c in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : ' ');
        }
        return sb.Append(' ').ToString();
    }
}

/// <summary>0085: tipos de registro do histórico do item.</summary>
public static class WorkItemLogKind
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Status = "status";
    public const string Task = "task";
    public const string Request = "request";
    public const string Cancel = "cancel";
    public const string Undo = "undo";
    public const string Info = "info";
}

/// <summary>0085: tipos de link do item.</summary>
public static class WorkItemLinkKind
{
    public const string PullRequest = "pr";
    public const string Branch = "branch";
    public const string Commit = "commit";
    public const string Document = "doc";
    public const string Attachment = "attachment";
    public const string Timeline = "timeline";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
        { PullRequest, Branch, Commit, Document, Attachment, Timeline };
}

/// <summary>0085: link do item (PR, branch, commit, documento, anexo, timeline) — JSON no próprio item.</summary>
public class WorkItemLink
{
    public string Kind { get; set; } = WorkItemLinkKind.Document;
    public string? Title { get; set; }
    public string Url { get; set; } = string.Empty;
}

/// <summary>0085: como desfazer a sub-tarefa concluída (ex.: <c>{action:"close-pr", params:{repository, number}}</c>).</summary>
public class WorkTaskUndo
{
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, string> Params { get; set; } = [];
}
