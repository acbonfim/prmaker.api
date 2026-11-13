using System.Globalization;
using System.Text;
using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

/// <summary>0075: tipos de "Situação atual" do card (o que está sendo aguardado).</summary>
public static class CardSituationKind
{
    public const string None = "none";
    public const string Ticket = "ticket";
    /// <summary>Etapa de chamado da pessoa sem chamado aberto (ainda não aberto, ou o anterior fechou sem resolução).</summary>
    public const string TicketToOpen = "ticket-open";
    public const string PullRequestReview = "pr-review";
    public const string QaValidation = "qa-validation";
    public const string Answer = "answer";
    public const string User = "user";

    /// <summary>Tipos que viram entrada na Timeline quando o card entra/sai deles (pergunta e "aguardando você" já têm a sua).</summary>
    public static bool Milestone(string? kind) => kind is Ticket or TicketToOpen or PullRequestReview or QaValidation;

    /// <summary>
    /// A situação vira entrada na Timeline e resumo na discussion? Chamado, PR, QA e — na correção — etapa prevista no
    /// plano para quem trata o card (ex.: configurar no sistema). Trava interna ("liberar permissão", VPN) não.
    /// </summary>
    public static bool Milestone(CardSituationResponse r) => Milestone(r.Kind) || (r.Kind == User && r.PlannedTask);

    /// <summary>
    /// Publica na discussion do DevOps? Em chamado, PR ou QA sempre; fora deles só atualiza o comentário que já existe
    /// (para não ficar dizendo "aguardando" depois que a espera acabou).
    /// </summary>
    public static bool PostToDevOps(CardSituationResponse r, bool hasComment) => Milestone(r) || hasComment;

    public static string Title(string kind) => kind switch
    {
        Ticket => "Aguardando chamado",
        TicketToOpen => "Aguardando abrir chamado",
        PullRequestReview => "Aguardando aprovação e merge",
        QaValidation => "Validar em QA",
        Answer => "Aguardando resposta",
        User => "Aguardando você",
        _ => "Nada aguardando"
    };

    public static string Icon(string kind) => kind switch
    {
        Ticket or TicketToOpen => "🎫",
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
    /// <summary>r5 (1.5): como chamar a entrega nos textos — "correção" (Bug) ou "implementação" (User Story).</summary>
    public string Delivery { get; set; } = "correção";
    public DateTimeOffset? Since { get; set; }
    /// <summary>Outras esperas ao mesmo tempo (ex.: chamado e PR), além da principal.</summary>
    public List<string> AlsoWaiting { get; set; } = [];

    public Guid? PlanId { get; set; }
    public string? Phase { get; set; }
    public string? PlanTitle { get; set; }
    public string? PlanStatus { get; set; }
    public string? StepKey { get; set; }
    public string? StepTitle { get; set; }
    /// <summary>Arquivos da etapa principal (no chamado a abrir: o texto do chamado e o script).</summary>
    public List<string> StepFiles { get; set; } = [];

    public List<string> Done { get; set; } = [];
    public List<CardSituationPullRequest> PullRequests { get; set; } = [];
    public List<CardSituationTicket> Tickets { get; set; } = [];
    public List<string> Questions { get; set; } = [];
    public List<string> UserSteps { get; set; } = [];
    /// <summary>Correção esperando uma etapa prevista no plano para quem trata o card (não uma trava da skill).</summary>
    public bool PlannedTask { get; set; }

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

    /// <summary>0076: a publicação na discussion é automática para este card (preferência do dono do plano ou padrão global).</summary>
    public bool AutoPost { get; set; }
    /// <summary>0078: de onde vem o modo: card (escolhido para este card) | user (preferência do dono) | global.</summary>
    public string AutoPostSource { get; set; } = "global";
    /// <summary>0076: o último resumo publicado na discussion.</summary>
    public DateTimeOffset? PublishedAt { get; set; }
    public string? PublishedBy { get; set; }
    public string? PublishedMarkdown { get; set; }
    /// <summary>0076: há um resumo diferente do publicado, pronto para a discussion.</summary>
    public bool PublishPending { get; set; }

    /// <summary>Resumo pronto para copiar/handover/Timeline.</summary>
    public string Markdown { get; set; } = string.Empty;
}

public static class CardSituation
{
    private const int MaxChecklistLength = 2_000;
    /// <summary>
    /// Folga do contexto da passagem: gravado até 10 min antes da mudança de situação ainda vale (a skill pode gravá-lo logo
    /// antes de anexar o PR/chamado).
    /// </summary>
    private static readonly TimeSpan HandoffGrace = TimeSpan.FromMinutes(10);

    /// <param name="isQaBranch">A branch de destino é a de QA (config do PRMake)?</param>
    public static CardSituationResponse Build(ExecutionPlan? plan, IReadOnlyList<ExecutionLink> links, IReadOnlyList<ExecutionQuestion> questions,
        IReadOnlyList<ExecutionArtifact> artifacts, Func<string?, bool> isQaBranch, string cardNumber)
    {
        var r = new CardSituationResponse { CardNumber = cardNumber, Delivery = PlanTerms.Delivery(plan?.Kind) };
        if (plan is null)
            return Finish(r);

        r.PlanId = plan.Id;
        r.Phase = plan.Phase;
        r.PlanTitle = plan.Title;
        r.PlanStatus = plan.Status;
        r.Handoff = plan.Handoff;
        r.HandoffAt = plan.HandoffAt;
        r.HandoffBy = plan.HandoffBy;
        var now = DateTimeOffset.UtcNow;
        r.DevTestInQaAt = plan.DevTestInQaAt;
        r.PublishedAt = plan.SituationPostedAt;
        r.PublishedBy = plan.SituationPostedBy;
        r.PublishedMarkdown = plan.SituationPostedText;
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

        // Validação em QA na frente só quando nada antes dela falta (ex.: o chamado de dados ainda aberto vem primeiro).
        var qaWait = qaStep is not null && !ExecutionStatus.IsStepFinished(qaStep.Status) && (plan.DevTestInQaAt is not null || qaMerged || pending.Contains(qaStep))
            ? (CardSituationKind.QaValidation, plan.DevTestInQaAt ?? r.PullRequests.Where(p => p.IsQa).Max(p => p.MergedAt) ?? qaStep.UpdatedAt, (ExecutionStep?)qaStep)
            : ((string, DateTimeOffset?, ExecutionStep?)?)null;
        var qaReady = qaStep is not null && qaStep.DependsOn.All(k => plan.Steps.FirstOrDefault(s => s.Key == k) is not { } d || ExecutionStatus.IsStepFinished(d.Status));
        if (qaWait is { } first && qaReady)
            waits.Add(first);

        var ticketSteps = plan.Steps.Where(s => s.Kind == ExecutionStepKind.Ticket && !ExecutionStatus.IsStepFinished(s.Status)
                                                && links.Any(l => l.StepKey == s.Key && l.Kind == ExecutionLinkKind.Ticket && l.Status == ExecutionLinkStatus.Open)).ToList();
        if (ticketSteps.Count > 0)
            waits.Add((CardSituationKind.Ticket, r.Tickets.Min(t => (DateTimeOffset?)t.OpenedAt), ticketSteps[0]));

        // Chamado da pessoa ainda por abrir (na vez dela, sem chamado aberto): "Aguardando abrir chamado".
        var toOpen = pending.Where(s => s.Kind == ExecutionStepKind.Ticket && !ticketSteps.Contains(s)).ToList();
        if (toOpen.Count > 0)
            waits.Add((CardSituationKind.TicketToOpen, toOpen.Min(s => (DateTimeOffset?)s.UpdatedAt), toOpen[0]));

        var prSteps = plan.Steps.Where(s => s.Kind == ExecutionStepKind.PullRequest && !ExecutionStatus.IsStepFinished(s.Status)
                                            && links.Any(l => l.StepKey == s.Key && l.Kind == ExecutionLinkKind.PullRequest && l.Status == ExecutionLinkStatus.Open)).ToList();
        if (prSteps.Count > 0)
            waits.Add((CardSituationKind.PullRequestReview,
                r.PullRequests.Where(p => p.Status == ExecutionLinkStatus.Open).Min(p => (DateTimeOffset?)p.OpenedAt), prSteps[0]));

        if (open.Count > 0)
            waits.Add((CardSituationKind.Answer, open.Min(q => q.CreatedAt), null));

        var userSteps = pending.Where(s => s != qaStep && !ticketSteps.Contains(s) && !toOpen.Contains(s)).ToList();
        r.UserSteps = userSteps.Select(s => s.Status == ExecutionStatus.Waiting && s.StatusReason is not null ? $"{s.Title}: {s.StatusReason}" : s.Title).ToList();
        if (userSteps.Count > 0)
        {
            // Etapa do usuário prevista no plano de correção (configurar, validar...) primeiro; trava da skill depois.
            var planned = plan.Phase == ExecutionPhase.Correction ? userSteps.FirstOrDefault(s => s.Executor == ExecutionExecutor.User) : null;
            waits.Add((CardSituationKind.User, userSteps.Min(s => (DateTimeOffset?)s.UpdatedAt), planned ?? userSteps[0]));
            r.PlannedTask = planned is not null && waits.Count == 1;
        }
        if (qaWait is { } last && !qaReady)
            waits.Add(last);

        if (waits.Count > 0)
        {
            var main = waits[0];
            r.Kind = main.Kind;
            r.Since = main.Since;
            r.StepKey = main.Step?.Key;
            r.StepTitle = main.Step?.Title;
            if (main.Kind == CardSituationKind.TicketToOpen && main.Step is { } step)
                r.StepFiles = artifacts.Where(a => a.NoteId is null && a.StepKey == step.Key).Select(a => a.Name).OrderBy(n => n).ToList();
            r.AlsoWaiting = waits.Skip(1).Select(w => CardSituationKind.Title(w.Kind)).Distinct().ToList();
        }
        if (r.Kind == CardSituationKind.QaValidation)
        {
            var checklist = plan.Steps.FirstOrDefault(s => s.Key == "impacto-na-base")?.Checkpoint?.Trim();
            r.QaChecklist = checklist is { Length: > MaxChecklistLength } ? checklist[..MaxChecklistLength] + "…" : checklist;
        }
        r.Next = NextText(r, plan);
        if (HandoffOutdated(plan, r.Kind, now))
        {
            // O contexto foi escrito para uma espera que já mudou (PR mesclado, chamado anexado...): não repete texto velho.
            r.Handoff = null;
            r.HandoffAt = null;
            r.HandoffBy = null;
        }
        return Finish(r);
    }

    /// <summary>
    /// O contexto da passagem é de antes da situação atual: gravado (com folga) antes de o card entrar nela — a mudança
    /// registrada (<see cref="ExecutionPlan.SituationAt"/>) ou, ainda não registrada, agora.
    /// </summary>
    public static bool HandoffOutdated(ExecutionPlan plan, string kind, DateTimeOffset now)
    {
        if (plan.HandoffAt is not { } at || plan.SituationKind is null)
            return false;
        var changedAt = plan.SituationKind == kind ? plan.SituationAt : now;
        return changedAt is { } c && at < c - HandoffGrace;
    }

    private static string? NextText(CardSituationResponse r, ExecutionPlan plan) => r.Kind switch
    {
        CardSituationKind.Ticket => $"Quando o chamado for atendido, marco como resolvido no PRMake para a {r.Delivery} seguir.",
        CardSituationKind.TicketToOpen => $"Abro o chamado e anexo à etapa no PRMake (Anexar chamado); a {r.Delivery} segue quando ele for atendido.",
        CardSituationKind.PullRequestReview => $"Com a revisão e o merge dos PRs em QA, a {r.Delivery} segue para validação.",
        CardSituationKind.QaValidation => r.DevTestInQaAt is null
            ? $"Movo o card para Dev Test in QA, valido a {r.Delivery} em QA e concluo a etapa no PRMake. Em seguida o card vai para Ready for QA."
            : $"Valido a {r.Delivery} em QA e concluo a etapa no PRMake. Em seguida o card vai para Ready for QA.",
        CardSituationKind.Answer => "Respondo as perguntas no PRMake para a análise seguir.",
        CardSituationKind.User when r.PlannedTask => "Faço o que a etapa pede (o passo a passo está nos arquivos do plano) e concluo no PRMake; em seguida vem o fechamento do card.",
        CardSituationKind.User => "Concluo no PRMake o que a etapa pede.",
        _ => null
    };

    /// <summary>Monta o markdown (também usado depois de completar o estado do DevOps).</summary>
    public static CardSituationResponse Finish(CardSituationResponse r)
    {
        r.Title = CardSituationKind.Title(r.Kind);
        r.Icon = CardSituationKind.Icon(r.Kind);
        r.Markdown = Markdown(r);
        return r;
    }

    private static string Headline(string kind) => kind switch
    {
        CardSituationKind.Ticket => "Aguardando o atendimento do chamado",
        CardSituationKind.TicketToOpen => "Aguardando abrir chamado",
        CardSituationKind.PullRequestReview => "Aguardando revisão e merge dos PRs",
        CardSituationKind.QaValidation => "Aguardando validação em QA",
        CardSituationKind.Answer => "Aguardando resposta",
        CardSituationKind.User => "Aguardando uma ação no PRMake",
        _ => "Sem pendências no momento"
    };

    /// <summary>
    /// Resumo em texto corrido, como uma pessoa escreveria na discussion: o que aconteceu, o que falta e o próximo passo.
    /// Vai para a Timeline, para a discussion do DevOps, para o "Copiar resumo" e para o handover.
    /// </summary>
    public static string Markdown(CardSituationResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Kind == CardSituationKind.User && r.PlannedTask && r.StepTitle is not null
            ? $"**Aguardando: {r.StepTitle}**" : $"**{Headline(r.Kind)}**").AppendLine();
        var manyRepos = r.PullRequests.Select(p => p.Repository).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

        switch (r.Kind)
        {
            case CardSituationKind.QaValidation:
            {
                var qa = r.PullRequests.Where(p => p.IsQa && p.Status == ExecutionLinkStatus.Merged).OrderByDescending(p => p.MergedAt).FirstOrDefault();
                var text = new StringBuilder($"A {r.Delivery} foi publicada em QA");
                if (qa?.MergedAt is { } at) text.Append($" em {Short(at)}");
                text.Append('.');
                if (r.DevTestInQaAt is { } moved)
                    text.Append($" O card está em Dev Test in QA desde {Short(moved)}.");
                else if (r.DevTestInQaError is not null)
                    text.Append($" Não foi possível mover o card para Dev Test in QA automaticamente ({r.DevTestInQaError.TrimEnd('.')}).");
                sb.AppendLine(text.ToString());
                if (r.PullRequests.Count > 0)
                    sb.AppendLine().AppendLine($"PRs mesclados: {Join(r.PullRequests.Where(p => p.Status == ExecutionLinkStatus.Merged).Select(p => PrRef(p, manyRepos)))}.");
                if (!string.IsNullOrWhiteSpace(r.QaChecklist))
                    sb.AppendLine().AppendLine("**O que validar**").AppendLine(r.QaChecklist);
                break;
            }
            case CardSituationKind.PullRequestReview:
            {
                var open = r.PullRequests.Where(p => p.Status == ExecutionLinkStatus.Open).ToList();
                sb.AppendLine(open.Count == 1 ? $"O PR da {r.Delivery} está aberto desde {Short(open[0].OpenedAt)}:" : $"Os PRs da {r.Delivery} estão abertos desde {Short(open.Min(p => p.OpenedAt))}:");
                foreach (var p in open)
                    sb.AppendLine($"- {PrRef(p, manyRepos)}: {Review(p)}");
                var merged = r.PullRequests.Where(p => p.Status == ExecutionLinkStatus.Merged).ToList();
                if (merged.Count > 0)
                    sb.AppendLine().AppendLine($"Já mesclados: {Join(merged.Select(p => PrRef(p, manyRepos)))}.");
                break;
            }
            case CardSituationKind.Ticket:
                foreach (var t in r.Tickets)
                {
                    var files = t.Files.Count == 0 ? "" : $", com {(t.Files.Count == 1 ? "o arquivo" : "os arquivos")} {Join(t.Files.Select(f => $"`{f}`"))}";
                    sb.AppendLine($"Chamado [{t.Title}]({t.Url}) aberto em {Short(t.OpenedAt)} por {t.OpenedBy}{files}.");
                }
                sb.AppendLine().AppendLine($"A {r.Delivery} segue quando o chamado for atendido.");
                break;
            case CardSituationKind.TicketToOpen:
            {
                var files = r.StepFiles.Count == 0 ? "" : $" Arquivos do chamado no plano: {Join(r.StepFiles.Select(f => $"`{f}`"))}.";
                sb.AppendLine($"{r.StepTitle}.{files}");
                break;
            }
            case CardSituationKind.Answer:
                sb.AppendLine(r.Questions.Count == 1 ? "Há uma pergunta aguardando resposta no PRMake:" : "Há perguntas aguardando resposta no PRMake:");
                foreach (var q in r.Questions) sb.AppendLine($"- {q}");
                break;
            case CardSituationKind.User when r.PlannedTask:
                sb.AppendLine($"A {r.Delivery} depende desta etapa, que eu faço fora do código, antes do fechamento.");
                if (r.UserSteps.Count > 1)
                {
                    sb.AppendLine().AppendLine("Também pendente:");
                    foreach (var u in r.UserSteps.Skip(1)) sb.AppendLine($"- {u}");
                }
                break;
            case CardSituationKind.User:
                sb.AppendLine("Pendente no PRMake:");
                foreach (var u in r.UserSteps) sb.AppendLine($"- {u}");
                break;
            default:
                sb.AppendLine("Nada aguardando; o plano segue normalmente.");
                break;
        }

        if (r.AlsoWaiting.Count > 0)
            sb.AppendLine().AppendLine($"Também em andamento: {Join(r.AlsoWaiting.Select(Lower))}.");
        if (!string.IsNullOrWhiteSpace(r.Handoff))
            sb.AppendLine().AppendLine("**Contexto**").AppendLine(r.Handoff.Trim());
        if (r.Next is not null)
            sb.AppendLine().AppendLine($"**Próximo passo:** {r.Next}");
        return sb.ToString().TrimEnd();
    }

    private static string PrRef(CardSituationPullRequest p, bool withRepository)
    {
        var label = p.Number is { } n ? $"#{n}" : p.Title ?? "PR";
        var target = p.TargetBranch is null ? "" : $" ({(withRepository && p.Repository is not null ? $"{p.Repository} → " : "")}{p.TargetBranch})";
        return $"[{label}]({p.Url}){target}";
    }

    private static string Review(CardSituationPullRequest p)
    {
        var parts = new List<string>();
        parts.Add(p.Approvals switch { null => "revisão ainda não lida", 0 => "sem aprovação", 1 => "1 aprovação", var n => $"{n} aprovações" });
        if (p.ChangesRequested is > 0) parts.Add(p.ChangesRequested == 1 ? "1 pedido de alteração" : $"{p.ChangesRequested} pedidos de alteração");
        if (!string.IsNullOrWhiteSpace(p.ReviewersPending)) parts.Add($"aguardando revisão de {p.ReviewersPending}");
        var ci = p.Checks switch
        {
            ExecutionLinkChecks.Success => "CI passou",
            ExecutionLinkChecks.Failure => "CI falhou",
            ExecutionLinkChecks.Pending => "CI rodando",
            _ => null
        };
        if (ci is not null) parts.Add(ci);
        return string.Join(", ", parts) + ".";
    }

    private static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list.Take(list.Count - 1)) + " e " + list[^1];
    }

    private static string Lower(string title) => title.Length == 0 ? title : char.ToLowerInvariant(title[0]) + title[1..];

    private static string Short(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, Brasilia).ToString("dd/MM 'às' HH:mm", CultureInfo.InvariantCulture);

    // Horário de Brasília (o time está aqui; a Timeline mostra datas assim).
    public static readonly TimeZoneInfo Brasilia = FindZone();

    private static TimeZoneInfo FindZone()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length <= 160 ? line : line[..160] + "…";
    }
}
