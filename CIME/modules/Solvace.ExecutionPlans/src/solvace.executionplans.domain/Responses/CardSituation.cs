using System.Globalization;
using System.Text;
using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

/// <summary>0075: tipos de "Situação atual" do card (o que está sendo aguardado).</summary>
public static class CardSituationKind
{
    public const string None = "none";
    public const string Ticket = "ticket";
    public const string PullRequestReview = "pr-review";
    public const string QaValidation = "qa-validation";
    public const string Answer = "answer";
    public const string User = "user";

    /// <summary>Tipos que viram entrada na Timeline quando o card entra/sai deles (pergunta e "aguardando você" já têm a sua).</summary>
    public static bool Milestone(string? kind) => kind is Ticket or PullRequestReview or QaValidation;

    public static string Title(string kind) => kind switch
    {
        Ticket => "Aguardando chamado",
        PullRequestReview => "Aguardando aprovação e merge",
        QaValidation => "Validar em QA",
        Answer => "Aguardando resposta",
        User => "Aguardando você",
        _ => "Nada aguardando"
    };

    public static string Icon(string kind) => kind switch
    {
        Ticket => "🎫",
        PullRequestReview => "🔀",
        QaValidation => "🧪",
        Answer => "❓",
        User => "⚠️",
        _ => "▶️"
    };
}

public class CardSituationPullRequest
{
    public string? Repository { get; set; }
    public int? Number { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? TargetBranch { get; set; }
    public string? Status { get; set; }
    public bool IsQa { get; set; }
    public string? Checks { get; set; }
    public int? Approvals { get; set; }
    public int? ChangesRequested { get; set; }
    public string? ReviewersPending { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
}

public class CardSituationTicket
{
    public string Url { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Status { get; set; }
    public string? StepTitle { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public string OpenedBy { get; set; } = string.Empty;
    public List<string> Files { get; set; } = [];
}

/// <summary>
/// 0075: "Situação atual" do card — em que pé está, o que é aguardado, desde quando, de quem e o que vem depois. Os
/// fatos saem do plano (sem IA); o <see cref="Handoff"/> é o contexto que a skill deixou ao entrar em espera.
/// </summary>
public class CardSituationResponse
{
    public string CardNumber { get; set; } = string.Empty;
    public string Kind { get; set; } = CardSituationKind.None;
    public string Title { get; set; } = CardSituationKind.Title(CardSituationKind.None);
    public string Icon { get; set; } = CardSituationKind.Icon(CardSituationKind.None);
    public DateTimeOffset? Since { get; set; }
    /// <summary>Outras esperas ao mesmo tempo (ex.: chamado e PR), além da principal.</summary>
    public List<string> AlsoWaiting { get; set; } = [];

    public Guid? PlanId { get; set; }
    public string? Phase { get; set; }
    public string? PlanTitle { get; set; }
    public string? PlanStatus { get; set; }
    public string? StepKey { get; set; }
    public string? StepTitle { get; set; }

    public List<string> Done { get; set; } = [];
    public List<CardSituationPullRequest> PullRequests { get; set; } = [];
    public List<CardSituationTicket> Tickets { get; set; } = [];
    public List<string> Questions { get; set; } = [];
    public List<string> UserSteps { get; set; } = [];

    public DateTimeOffset? DevTestInQaAt { get; set; }
    public string? DevTestInQaError { get; set; }
    /// <summary>Roteiro de validação em QA: o checkpoint de <c>impacto-na-base</c> (o que mais usa os pontos alterados).</summary>
    public string? QaChecklist { get; set; }

    public string? DevOpsState { get; set; }
    public string? DevOpsColumn { get; set; }

    public string? Handoff { get; set; }
    public DateTimeOffset? HandoffAt { get; set; }
    public string? HandoffBy { get; set; }

    public string? Next { get; set; }
    /// <summary>Resumo pronto para copiar/handover/Timeline.</summary>
    public string Markdown { get; set; } = string.Empty;
}

public static class CardSituation
{
    private const int MaxChecklistLength = 2_000;

    /// <param name="isQaBranch">A branch de destino é a de QA (config do PRMake)?</param>
    public static CardSituationResponse Build(ExecutionPlan? plan, IReadOnlyList<ExecutionLink> links, IReadOnlyList<ExecutionQuestion> questions,
        IReadOnlyList<ExecutionArtifact> artifacts, Func<string?, bool> isQaBranch, string cardNumber)
    {
        var r = new CardSituationResponse { CardNumber = cardNumber };
        if (plan is null)
            return Finish(r);

        r.PlanId = plan.Id;
        r.Phase = plan.Phase;
        r.PlanTitle = plan.Title;
        r.PlanStatus = plan.Status;
        r.Handoff = plan.Handoff;
        r.HandoffAt = plan.HandoffAt;
        r.HandoffBy = plan.HandoffBy;
        r.DevTestInQaAt = plan.DevTestInQaAt;
        r.DevTestInQaError = plan.DevTestInQaAt is null ? plan.DevTestInQaError : null;
        r.Done = plan.Steps.Where(s => s.Status == ExecutionStatus.Completed).OrderBy(s => s.Order).Select(s => s.Title).ToList();

        var titles = plan.Steps.ToDictionary(s => s.Key, s => s.Title);
        r.PullRequests = links.Where(l => l.Kind == ExecutionLinkKind.PullRequest && l.Status != ExecutionLinkStatus.Closed)
            .OrderBy(l => l.CreatedAt)
            .Select(l => new CardSituationPullRequest
            {
                Repository = l.Repository, Number = l.PullRequestNumber, Url = l.Url, Title = l.DisplayName, TargetBranch = l.TargetBranch,
                Status = l.Status, IsQa = isQaBranch(l.TargetBranch), Checks = l.ChecksStatus, Approvals = l.Approvals,
                ChangesRequested = l.ChangesRequested, ReviewersPending = l.ReviewersPending, OpenedAt = l.CreatedAt,
                MergedAt = l.Status == ExecutionLinkStatus.Merged ? l.StatusChangedAt : null
            }).ToList();
        r.Tickets = links.Where(l => l.Kind == ExecutionLinkKind.Ticket && l.Status == ExecutionLinkStatus.Open)
            .OrderBy(l => l.CreatedAt)
            .Select(l => new CardSituationTicket
            {
                Url = l.Url, Title = l.DisplayName, Status = l.Status, OpenedAt = l.CreatedAt, OpenedBy = l.CreatedBy,
                StepTitle = titles.GetValueOrDefault(l.StepKey),
                Files = artifacts.Where(a => a.NoteId is null && a.StepKey == l.StepKey).Select(a => a.Name).OrderBy(n => n).ToList()
            }).ToList();
        var open = questions.Where(q => q.Status == ExecutionQuestionStatus.Open).OrderBy(q => q.Order).ToList();
        r.Questions = open.Select(q => $"{q.Order}. {FirstLine(q.Text)}").ToList();

        if (plan.IsFinished || plan.Status == ExecutionStatus.Failed)
            return Finish(r);

        var pending = plan.StepsPendingForUser().ToList();
        var qaStep = plan.QaValidationStep;
        var qaMerged = r.PullRequests.Any(p => p.IsQa && p.Status == ExecutionLinkStatus.Merged);
        var waits = new List<(string Kind, DateTimeOffset? Since, ExecutionStep? Step)>();

        if (qaStep is not null && !ExecutionStatus.IsStepFinished(qaStep.Status) && (plan.DevTestInQaAt is not null || qaMerged || pending.Contains(qaStep)))
            waits.Add((CardSituationKind.QaValidation,
                plan.DevTestInQaAt ?? r.PullRequests.Where(p => p.IsQa).Max(p => p.MergedAt) ?? qaStep.UpdatedAt, qaStep));

        var ticketSteps = plan.Steps.Where(s => s.Kind == ExecutionStepKind.Ticket && !ExecutionStatus.IsStepFinished(s.Status)
                                                && links.Any(l => l.StepKey == s.Key && l.Kind == ExecutionLinkKind.Ticket && l.Status == ExecutionLinkStatus.Open)).ToList();
        if (ticketSteps.Count > 0)
            waits.Add((CardSituationKind.Ticket, r.Tickets.Min(t => (DateTimeOffset?)t.OpenedAt), ticketSteps[0]));

        var prSteps = plan.Steps.Where(s => s.Kind == ExecutionStepKind.PullRequest && !ExecutionStatus.IsStepFinished(s.Status)
                                            && links.Any(l => l.StepKey == s.Key && l.Kind == ExecutionLinkKind.PullRequest && l.Status == ExecutionLinkStatus.Open)).ToList();
        if (prSteps.Count > 0)
            waits.Add((CardSituationKind.PullRequestReview,
                r.PullRequests.Where(p => p.Status == ExecutionLinkStatus.Open).Min(p => (DateTimeOffset?)p.OpenedAt), prSteps[0]));

        if (open.Count > 0)
            waits.Add((CardSituationKind.Answer, open.Min(q => q.CreatedAt), null));

        var userSteps = pending.Where(s => s != qaStep && !ticketSteps.Contains(s)).ToList();
        r.UserSteps = userSteps.Select(s => s.Status == ExecutionStatus.Waiting && s.StatusReason is not null ? $"{s.Title}: {s.StatusReason}" : s.Title).ToList();
        if (userSteps.Count > 0)
            waits.Add((CardSituationKind.User, userSteps.Min(s => (DateTimeOffset?)s.UpdatedAt), userSteps[0]));

        if (waits.Count > 0)
        {
            var main = waits[0];
            r.Kind = main.Kind;
            r.Since = main.Since;
            r.StepKey = main.Step?.Key;
            r.StepTitle = main.Step?.Title;
            r.AlsoWaiting = waits.Skip(1).Select(w => CardSituationKind.Title(w.Kind)).Distinct().ToList();
        }
        if (r.Kind == CardSituationKind.QaValidation)
        {
            var checklist = plan.Steps.FirstOrDefault(s => s.Key == "impacto-na-base")?.Checkpoint?.Trim();
            r.QaChecklist = checklist is { Length: > MaxChecklistLength } ? checklist[..MaxChecklistLength] + "…" : checklist;
        }
        r.Next = NextText(r, plan);
        return Finish(r);
    }

    private static string? NextText(CardSituationResponse r, ExecutionPlan plan)
    {
        // Depois = a etapa que espera por esta (dependsOn); sem ela, a próxima pendente.
        var pendingSteps = plan.Steps.Where(s => !ExecutionStatus.IsStepFinished(s.Status) && s.Key != r.StepKey).OrderBy(s => s.Order).ToList();
        var after = (pendingSteps.FirstOrDefault(s => r.StepKey is not null && s.DependsOn.Contains(r.StepKey)) ?? pendingSteps.FirstOrDefault())?.Title;
        var then = after is null ? "" : $" Depois: *{after}*.";
        return r.Kind switch
        {
            CardSituationKind.Ticket => "Quando o chamado for atendido, marque-o como *Resolvido* na etapa — ela conclui e o plano segue." + then,
            CardSituationKind.PullRequestReview => "Quando os PRs forem aprovados e mesclados (por outra pessoa), a etapa conclui sozinha." + then,
            CardSituationKind.QaValidation => (r.DevTestInQaAt is not null ? "Valide"
                                                  : r.DevTestInQaError is not null ? "O PRMake não conseguiu mover o card para *Dev Test in QA* (aviso acima): mova pelo menu *DevOps*, valide"
                                                  : "Mova o card para *Dev Test in QA*, valide")
                                              + " a correção em QA e conclua *" + (r.StepTitle ?? "Validar em QA") + "* — o card vai para *Ready for QA*.",
            CardSituationKind.Answer => "Responda as perguntas no PRMake (ou no Claude) — o plano segue com as respostas.",
            CardSituationKind.User => "Faça o que a etapa pede e conclua na tela (botão *Concluir*/*Já resolvi*).",
            _ => null
        };
    }

    /// <summary>Monta o markdown (também usado depois de completar o estado do DevOps).</summary>
    public static CardSituationResponse Finish(CardSituationResponse r)
    {
        r.Title = CardSituationKind.Title(r.Kind);
        r.Icon = CardSituationKind.Icon(r.Kind);
        r.Markdown = Markdown(r);
        return r;
    }

    public static string Markdown(CardSituationResponse r)
    {
        var sb = new StringBuilder();
        sb.Append($"{r.Icon} **Situação: {r.Title}**");
        if (r.Since is { } since) sb.Append($" · desde {Date(since)}");
        sb.AppendLine();
        if (r.AlsoWaiting.Count > 0) sb.AppendLine($"Também: {string.Join(" · ", r.AlsoWaiting)}");
        if (r.PlanTitle is not null)
            sb.AppendLine($"Plano de {(r.Phase == ExecutionPhase.Correction ? "correção" : "análise")}: {r.PlanTitle}{(r.StepTitle is null ? "" : $" — etapa *{r.StepTitle}*")}");
        if (r.DevOpsState is not null)
            sb.AppendLine($"DevOps: **{r.DevOpsState}**{(string.IsNullOrWhiteSpace(r.DevOpsColumn) ? "" : $" · coluna *{r.DevOpsColumn}*")}");

        if (r.Tickets.Count > 0)
        {
            sb.AppendLine().AppendLine("**Chamados abertos**");
            foreach (var t in r.Tickets)
                sb.AppendLine($"- [{t.Title}]({t.Url}) — aberto em {Date(t.OpenedAt)} por {t.OpenedBy}" +
                              (t.Files.Count > 0 ? $" · arquivos: {string.Join(", ", t.Files.Select(f => $"`{f}`"))}" : ""));
        }
        if (r.PullRequests.Count > 0)
        {
            sb.AppendLine().AppendLine("**Pull requests**");
            foreach (var p in r.PullRequests)
                sb.AppendLine($"- [{p.Title}]({p.Url}) — {PrState(p)}");
        }
        if (r.Kind == CardSituationKind.QaValidation)
        {
            sb.AppendLine().AppendLine("**Validação em QA**");
            sb.AppendLine(r.DevTestInQaAt is { } moved ? $"- Card em *Dev Test in QA* desde {Date(moved)}"
                : r.DevTestInQaError is not null ? $"- ⚠️ Não deu para mover para *Dev Test in QA*: {r.DevTestInQaError}"
                : "- Card ainda não foi para *Dev Test in QA*");
            if (!string.IsNullOrWhiteSpace(r.QaChecklist))
                sb.AppendLine().AppendLine("*Roteiro (o que mais usa os pontos alterados):*").AppendLine(r.QaChecklist);
        }
        if (r.Questions.Count > 0)
        {
            sb.AppendLine().AppendLine("**Perguntas abertas**");
            foreach (var q in r.Questions) sb.AppendLine($"- {q}");
        }
        if (r.UserSteps.Count > 0)
        {
            sb.AppendLine().AppendLine("**Com você**");
            foreach (var s in r.UserSteps) sb.AppendLine($"- {s}");
        }
        if (r.Done.Count > 0)
            sb.AppendLine().AppendLine($"**Já feito:** {string.Join(" · ", r.Done)}");
        if (!string.IsNullOrWhiteSpace(r.Handoff))
            sb.AppendLine().AppendLine($"**Contexto da passagem**{(r.HandoffBy is null ? "" : $" ({r.HandoffBy}, {Date(r.HandoffAt!.Value)})")}").AppendLine(r.Handoff);
        if (r.Next is not null)
            sb.AppendLine().AppendLine($"**Próximo passo:** {r.Next}");
        return sb.ToString().TrimEnd();
    }

    private static string PrState(CardSituationPullRequest p)
    {
        // O título do link já traz "→ destino" (sincronização do GitHub); só repete o destino quando não traz.
        var titled = p.TargetBranch is not null && (p.Title?.Contains($"→ {p.TargetBranch}", StringComparison.OrdinalIgnoreCase) ?? false);
        var target = (p.TargetBranch is null || titled ? "" : $"→ `{p.TargetBranch}` · ") + (p.IsQa ? "QA · " : "");
        if (p.Status == ExecutionLinkStatus.Merged)
            return $"{target}✅ mesclado{(p.MergedAt is { } m ? $" em {Date(m)}" : "")}";
        var parts = new List<string> { "aberto" };
        if (p.Approvals is > 0) parts.Add($"{p.Approvals} aprovação(ões)");
        if (p.ChangesRequested is > 0) parts.Add($"{p.ChangesRequested} pedido(s) de alteração");
        if (p.Approvals is 0 && p.ChangesRequested is 0 or null) parts.Add("sem aprovação");
        if (!string.IsNullOrWhiteSpace(p.ReviewersPending)) parts.Add($"aguardando revisão de {p.ReviewersPending}");
        parts.Add(p.Checks switch
        {
            ExecutionLinkChecks.Success => "CI ✅",
            ExecutionLinkChecks.Failure => "CI ❌",
            ExecutionLinkChecks.Pending => "CI ⏳",
            _ => "sem CI"
        });
        return target + string.Join(" · ", parts) + $" · aberto em {Date(p.OpenedAt)}";
    }

    // Horário de Brasília (o time está aqui; a Timeline mostra datas assim).
    private static readonly TimeZoneInfo Brasilia = FindZone();

    private static TimeZoneInfo FindZone()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");
    }

    private static string Date(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, Brasilia).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length <= 160 ? line : line[..160] + "…";
    }
}
