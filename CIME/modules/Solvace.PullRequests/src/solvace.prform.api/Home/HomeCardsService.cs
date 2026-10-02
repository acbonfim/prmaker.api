using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.infra.Contexts;
using solvace.prform.domain.Enums;
using solvace.prform.Infra.Contexts;
using solvace.timeline.infra.Contexts;

namespace solvace.prform.Home;

/// <summary>
/// Listas de cards da home (0051). Junta o que cada módulo sabe do card — registro e PRs (prform), Timeline,
/// plano de execução (execution) e handover — numa resposta só, para a home não fazer uma chamada por card.
/// Só lê; o status do DevOps vem à parte (<c>POST Azure/cards/summary</c>), porque depende da integração do usuário.
/// </summary>
public sealed partial class HomeCardsService(
    DefaultContext db,
    TimelineContext timeline,
    ExecutionPlanContext plans,
    AuthenticationContext auth)
{
    public const int MaxTake = 30;
    public const int MaxParticipants = 8;

    /// <summary>Quantos cards cada fonte devolve na busca do "participei" (as mais recentes) antes de juntar.</summary>
    private const int SourceLimit = 150;
    private const int ExcerptLength = 220;

    public async Task<IReadOnlyList<HomeCardResponse>> GetAsync(Guid userId, string? scope, int take, CancellationToken ct)
    {
        take = Math.Clamp(take <= 0 ? 10 : take, 1, MaxTake);
        var myName = await auth.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct);

        List<string> cards;
        if (string.Equals(scope, HomeCardsScope.Participated, StringComparison.OrdinalIgnoreCase))
        {
            var touched = await CollectParticipationAsync(userId, myName, ct);
            var touchedCards = touched.Keys.ToList();
            // Os cards do próprio usuário ficam em "Seus últimos cards": as duas listas se completam.
            var owned = await db.PullRequests.AsNoTracking()
                .Where(x => x.UserId == userId && touchedCards.Contains(x.CardNumber))
                .Select(x => x.CardNumber)
                .ToListAsync(ct);
            var ownedSet = owned.ToHashSet();
            cards = touched
                .Where(kv => !ownedSet.Contains(kv.Key))
                .OrderByDescending(kv => kv.Value)
                .Take(take)
                .Select(kv => kv.Key)
                .ToList();
        }
        else
        {
            // Mesma regra do GetRecentByUser: cards registrados pelo usuário, do mais recente para o mais antigo.
            cards = await db.PullRequests.AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .Take(take)
                .Select(x => x.CardNumber)
                .ToListAsync(ct);
        }

        return cards.Count == 0 ? [] : await BuildAsync(cards, userId, myName, ct);
    }

    /// <summary>
    /// Cards em que o usuário mexeu → a última vez que mexeu: salvou o card, abriu PR, escreveu na Timeline, comentou
    /// no plano, criou/respondeu/pausou-continuou o plano ou salvou o handover. Colunas de auditoria guardam o
    /// externalId como texto; ações do plano feitas pela tela guardam só o nome (o mesmo nome completo da auth).
    /// </summary>
    private async Task<Dictionary<string, DateTimeOffset>> CollectParticipationAsync(Guid userId, string? myName, CancellationToken ct)
    {
        var me = userId.ToString().ToLower();
        var name = string.IsNullOrWhiteSpace(myName) ? null : myName.Trim().ToLower();
        var result = new Dictionary<string, DateTimeOffset>();

        void Add(string card, DateTimeOffset? at)
        {
            if (string.IsNullOrWhiteSpace(card) || at is null) return;
            if (!result.TryGetValue(card, out var current) || at.Value > current)
                result[card] = at.Value;
        }

        var registers = await db.PullRequests.AsNoTracking()
            .Where(x => (x.UpdatedBy != null && x.UpdatedBy.ToLower() == me)
                        || (x.CreatedBy != null && x.CreatedBy.ToLower() == me))
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .Take(SourceLimit)
            .Select(x => new { x.CardNumber, x.UpdatedBy, x.CreatedAt, x.UpdatedAt })
            .ToListAsync(ct);
        foreach (var r in registers)
        {
            var updatedByMe = string.Equals(r.UpdatedBy, me, StringComparison.OrdinalIgnoreCase);
            Add(r.CardNumber, updatedByMe ? r.UpdatedAt ?? r.CreatedAt : r.CreatedAt);
        }

        var prs = await db.PullRequestsGithub.AsNoTracking()
            .Where(x => x.UserId == userId && x.Status != PullRequestGithubStatus.Legacy)
            .GroupBy(x => x.CardNumber)
            .Select(g => new { Card = g.Key, At = g.Max(x => x.CreatedAt) })
            .OrderByDescending(x => x.At)
            .Take(SourceLimit)
            .ToListAsync(ct);
        prs.ForEach(x => Add(x.Card, x.At));

        var handovers = await db.Handovers.AsNoTracking()
            .Where(x => (x.CreatedBy != null && x.CreatedBy.ToLower() == me) || (x.UpdatedBy != null && x.UpdatedBy.ToLower() == me))
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .Take(SourceLimit)
            .Select(x => new { x.CardNumber, At = x.UpdatedAt ?? x.CreatedAt })
            .ToListAsync(ct);
        handovers.ForEach(x => Add(x.CardNumber, x.At));

        var entries = await timeline.TimelineEntries.AsNoTracking()
            .Where(x => x.UserId == userId)
            .GroupBy(x => x.CardNumber)
            .Select(g => new { Card = g.Key, At = g.Max(x => x.CreatedAt) })
            .OrderByDescending(x => x.At)
            .Take(SourceLimit)
            .ToListAsync(ct);
        entries.ForEach(x => Add(x.Card, x.At));

        var notes = await plans.Notes.AsNoTracking()
            .Where(x => x.AuthorUserId == userId && x.DeletedAt == null)
            .GroupBy(x => x.CardNumber)
            .Select(g => new { Card = g.Key, At = g.Max(x => x.CreatedAt) })
            .OrderByDescending(x => x.At)
            .Take(SourceLimit)
            .ToListAsync(ct);
        notes.ForEach(x => Add(x.Card, x.At));

        var createdPlans = await plans.Plans.AsNoTracking()
            .Where(x => x.CreatedByUserId == userId)
            .GroupBy(x => x.CardNumber)
            .Select(g => new { Card = g.Key, At = g.Max(x => x.UpdatedAt) })
            .OrderByDescending(x => x.At)
            .Take(SourceLimit)
            .ToListAsync(ct);
        createdPlans.ForEach(x => Add(x.Card, x.At));

        if (name is not null)
        {
            var answered = await (
                    from q in plans.Questions.AsNoTracking()
                    join p in plans.Plans.AsNoTracking() on q.PlanId equals p.Id
                    where q.AnsweredBy != null && q.AnsweredBy.ToLower() == name && q.AnsweredAt != null
                    group q by p.CardNumber into g
                    select new { Card = g.Key, At = g.Max(x => x.AnsweredAt) })
                .OrderByDescending(x => x.At)
                .Take(SourceLimit)
                .ToListAsync(ct);
            answered.ForEach(x => Add(x.Card, x.At));

            var resumed = await plans.Plans.AsNoTracking()
                .Where(x => x.ResumeRequestedBy != null && x.ResumeRequestedBy.ToLower() == name && x.ResumeRequestedAt != null)
                .GroupBy(x => x.CardNumber)
                .Select(g => new { Card = g.Key, At = g.Max(x => x.ResumeRequestedAt) })
                .OrderByDescending(x => x.At)
                .Take(SourceLimit)
                .ToListAsync(ct);
            resumed.ForEach(x => Add(x.Card, x.At));

            var statusChanged = await plans.Plans.AsNoTracking()
                .Where(x => x.StatusChangedBy != null && x.StatusChangedBy.ToLower() == name)
                .GroupBy(x => x.CardNumber)
                .Select(g => new { Card = g.Key, At = g.Max(x => x.UpdatedAt) })
                .OrderByDescending(x => x.At)
                .Take(SourceLimit)
                .ToListAsync(ct);
            statusChanged.ForEach(x => Add(x.Card, x.At));
        }

        return result;
    }

    /// <summary>Uma atividade no card (de qualquer pessoa): vira participante, "última atividade" e selo do usuário.</summary>
    private sealed record CardEvent(string Card, string Kind, string Role, string Text, Guid? UserId, string? UserName, DateTimeOffset At);

    private async Task<IReadOnlyList<HomeCardResponse>> BuildAsync(List<string> cards, Guid userId, string? myName, CancellationToken ct)
    {
        var events = new List<CardEvent>();

        // --- Registro do card + PRs do GitHub (prform) ---
        var registers = await db.PullRequests.AsNoTracking()
            .Where(x => cards.Contains(x.CardNumber))
            .Select(x => new
            {
                x.CardNumber,
                x.UserId,
                x.UpdatedBy,
                x.CreatedAt,
                x.UpdatedAt,
                HasDescription = x.Description != "",
                HasRootCause = x.RootCause != "",
                SummaryPublished = x.SummaryPublishedAt != null,
                LegacyRepositoryId = x.RepositoryId,
            })
            .ToListAsync(ct);
        foreach (var r in registers)
        {
            events.Add(new(r.CardNumber, HomeCardActivityKind.Register, HomeCardRole.Register, "Registrou o card", r.UserId, null, r.CreatedAt));
            if (r.UpdatedAt is { } updatedAt && updatedAt > r.CreatedAt.AddMinutes(1))
                events.Add(new(r.CardNumber, HomeCardActivityKind.Register, HomeCardRole.Register, "Atualizou o card",
                    ParseGuid(r.UpdatedBy) ?? r.UserId, null, updatedAt));
        }

        var prs = await db.PullRequestsGithub.AsNoTracking()
            .Where(x => cards.Contains(x.CardNumber))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.CardNumber, x.RepositoryId, x.GithubPrNumber, x.Url, x.Title, x.Status, x.IsDraft, x.UserId, x.CreatedAt })
            .ToListAsync(ct);
        foreach (var pr in prs.Where(p => p.Status != PullRequestGithubStatus.Legacy))
            events.Add(new(pr.CardNumber, HomeCardActivityKind.PullRequest, HomeCardRole.PullRequest,
                $"Abriu o PR #{pr.GithubPrNumber} em {pr.RepositoryId}", pr.UserId, null, pr.CreatedAt));

        var handovers = await db.Handovers.AsNoTracking()
            .Where(x => cards.Contains(x.CardNumber))
            .Select(x => new { x.CardNumber, x.CreatedBy, x.UpdatedBy, At = x.UpdatedAt ?? x.CreatedAt })
            .ToListAsync(ct);
        foreach (var h in handovers)
            events.Add(new(h.CardNumber, HomeCardActivityKind.Handover, HomeCardRole.Handover, "Salvou o handover",
                ParseGuid(h.UpdatedBy) ?? ParseGuid(h.CreatedBy), null, h.At));

        // --- Timeline: todas as entradas (leves) para autores/contagem; o texto só da última ---
        var entries = await timeline.TimelineEntries.AsNoTracking()
            .Where(x => cards.Contains(x.CardNumber))
            .Select(x => new { x.Id, x.CardNumber, x.UserId, x.UserName, x.CreatedAt })
            .ToListAsync(ct);
        var lastEntries = entries.GroupBy(e => e.CardNumber)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).First());
        var lastEntryIds = lastEntries.Values.Select(e => e.Id).ToList();
        var lastEntryTexts = await timeline.TimelineEntries.AsNoTracking()
            .Where(x => lastEntryIds.Contains(x.Id))
            .Select(x => new { x.Id, Text = x.Description.Substring(0, 1000) })
            .ToDictionaryAsync(x => x.Id, x => x.Text, ct);
        foreach (var e in entries)
            events.Add(new(e.CardNumber, HomeCardActivityKind.Timeline, HomeCardRole.Timeline,
                lastEntries[e.CardNumber].Id == e.Id ? Excerpt(lastEntryTexts.GetValueOrDefault(e.Id)) : "Escreveu na Timeline",
                e.UserId, e.UserName, e.CreatedAt));

        // --- Plano de execução: planos, etapas do mais recente, perguntas e comentários ---
        var cardPlans = await plans.Plans.AsNoTracking()
            .Where(x => cards.Contains(x.CardNumber))
            .Select(x => new
            {
                x.Id, x.CardNumber, x.Phase, x.Title, x.Status, x.StatusChangedBy, x.CreatedByUserId, x.CreatedBy,
                x.CreatedAt, x.UpdatedAt, x.ResumeRequestedBy, x.ResumeRequestedAt
            })
            .ToListAsync(ct);
        var latestPlans = cardPlans.GroupBy(p => p.CardNumber)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First());
        var planIds = cardPlans.Select(p => p.Id).ToList();
        var latestPlanIds = latestPlans.Values.Select(p => p.Id).ToList();

        var steps = await plans.Steps.AsNoTracking()
            .Where(s => latestPlanIds.Contains(s.PlanId))
            .OrderBy(s => s.Order)
            .Select(s => new { s.PlanId, s.Title, s.Status, s.WaitingOn, s.StatusReason })
            .ToListAsync(ct);
        var stepsByPlan = steps.GroupBy(s => s.PlanId).ToDictionary(g => g.Key, g => g.ToList());

        var questions = await plans.Questions.AsNoTracking()
            .Where(q => planIds.Contains(q.PlanId))
            .Select(q => new { q.PlanId, q.Status, q.AnsweredBy, q.AnsweredAt })
            .ToListAsync(ct);
        var planCard = cardPlans.ToDictionary(p => p.Id, p => p.CardNumber);
        var openQuestions = questions.Where(q => q.Status == ExecutionQuestionStatus.Open)
            .GroupBy(q => planCard[q.PlanId])
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var p in cardPlans)
        {
            var phase = p.Phase == ExecutionPhase.Correction ? "correção" : "análise";
            events.Add(new(p.CardNumber, HomeCardActivityKind.Plan, HomeCardRole.Plan, $"Criou o plano de {phase}",
                p.CreatedByUserId, p.CreatedBy, p.CreatedAt));
            if (!string.IsNullOrWhiteSpace(p.StatusChangedBy) && p.UpdatedAt > p.CreatedAt)
                events.Add(new(p.CardNumber, HomeCardActivityKind.Plan, HomeCardRole.Plan, $"Plano de {phase}: {PlanStatusLabel(p.Status)}",
                    null, p.StatusChangedBy, p.UpdatedAt));
            if (!string.IsNullOrWhiteSpace(p.ResumeRequestedBy) && p.ResumeRequestedAt is { } resumedAt)
                events.Add(new(p.CardNumber, HomeCardActivityKind.Plan, HomeCardRole.Plan, "Pediu para continuar o plano",
                    null, p.ResumeRequestedBy, resumedAt));
        }
        foreach (var q in questions.Where(q => !string.IsNullOrWhiteSpace(q.AnsweredBy) && q.AnsweredAt is not null))
            events.Add(new(planCard[q.PlanId], HomeCardActivityKind.Plan, HomeCardRole.Plan, "Respondeu uma pergunta do plano",
                null, q.AnsweredBy, q.AnsweredAt!.Value));

        var notes = await plans.Notes.AsNoTracking()
            .Where(n => cards.Contains(n.CardNumber) && n.DeletedAt == null)
            .Select(n => new { n.CardNumber, n.Number, n.AuthorUserId, n.AuthorName, n.CreatedAt, Text = n.Text.Substring(0, 1000) })
            .ToListAsync(ct);
        foreach (var n in notes)
            events.Add(new(n.CardNumber, HomeCardActivityKind.Note, HomeCardRole.Plan, $"Comentou no plano (#{n.Number})",
                n.AuthorUserId, n.AuthorName, n.CreatedAt));

        // --- Pessoas: nome pelo externalId; ações gravadas só com o nome viram a mesma pessoa quando o nome bate ---
        var people = await ResolvePeopleAsync(events, ct);
        var myKey = userId.ToString();
        var myNameKey = string.IsNullOrWhiteSpace(myName) ? null : "n:" + myName.Trim().ToLowerInvariant();

        string? PersonKey(CardEvent e) =>
            e.UserId is { } id ? id.ToString()
            : string.IsNullOrWhiteSpace(e.UserName) ? null
            : people.IdsByName.TryGetValue(e.UserName.Trim().ToLowerInvariant(), out var mapped) ? mapped.ToString()
            : "n:" + e.UserName.Trim().ToLowerInvariant();

        bool IsMe(string? key) => key is not null && (key == myKey || key == myNameKey);

        var registerByCard = registers.ToDictionary(r => r.CardNumber);
        var eventsByCard = events.GroupBy(e => e.Card).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<HomeCardResponse>(cards.Count);
        foreach (var card in cards)
        {
            var cardEvents = eventsByCard.GetValueOrDefault(card) ?? [];
            registerByCard.TryGetValue(card, out var register);
            var ownerId = register?.UserId;

            // Envolvidos: dono primeiro, depois pela atividade mais recente.
            var participants = cardEvents
                .Select(e => new { Key = PersonKey(e), e })
                .Where(x => x.Key is not null)
                .GroupBy(x => x.Key!)
                .Select(g =>
                {
                    var userIdOf = g.Select(x => x.e.UserId).FirstOrDefault(id => id is not null)
                                   ?? (Guid.TryParse(g.Key, out var parsed) ? parsed : null);
                    var nameOf = (userIdOf is { } uid ? people.NamesById.GetValueOrDefault(uid) : null)
                                 ?? g.Select(x => x.e.UserName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim()
                                 ?? string.Empty;
                    return new
                    {
                        Participant = new HomeCardParticipant(userIdOf, nameOf, ownerId is not null && userIdOf == ownerId),
                        LastAt = g.Max(x => x.e.At)
                    };
                })
                .OrderByDescending(x => x.Participant.IsOwner)
                .ThenByDescending(x => x.LastAt)
                .Select(x => x.Participant)
                .ToList();

            var myEvents = cardEvents.Where(e => IsMe(PersonKey(e))).ToList();
            var last = cardEvents.OrderByDescending(e => e.At).FirstOrDefault();

            latestPlans.TryGetValue(card, out var plan);
            HomeCardPlan? planResponse = null;
            if (plan is not null)
            {
                var planSteps = stepsByPlan.GetValueOrDefault(plan.Id) ?? [];
                var counted = planSteps.Where(s => s.Status != ExecutionStatus.Cancelled).ToList();
                var current = planSteps.FirstOrDefault(s => s.Status is ExecutionStatus.Running or ExecutionStatus.Waiting)
                              ?? (plan.Status is ExecutionStatus.Running or ExecutionStatus.Paused or ExecutionStatus.Pending
                                  ? planSteps.FirstOrDefault(s => s.Status == ExecutionStatus.Pending)
                                  : null);
                planResponse = new HomeCardPlan
                {
                    Id = plan.Id,
                    Phase = plan.Phase,
                    Title = plan.Title,
                    Status = plan.Status,
                    StepsDone = counted.Count(s => s.Status == ExecutionStatus.Completed),
                    StepsTotal = counted.Count,
                    CurrentStep = current?.Title,
                    CurrentStepStatus = current?.Status,
                    WaitingOn = current?.Status == ExecutionStatus.Waiting ? current.WaitingOn : null,
                    WaitingReason = current?.Status == ExecutionStatus.Waiting ? current.StatusReason : null,
                    OpenQuestions = openQuestions.GetValueOrDefault(card),
                    Phases = cardPlans.Where(p => p.CardNumber == card).Select(p => p.Phase).Distinct().ToList(),
                    UpdatedAt = plan.UpdatedAt,
                };
            }

            var cardEntries = entries.Where(e => e.CardNumber == card).ToList();
            HomeCardEntry? lastEntry = null;
            if (lastEntries.TryGetValue(card, out var le))
                lastEntry = new HomeCardEntry(IdFor(le.UserId, le.UserName), NameFor(le.UserId, le.UserName), Excerpt(lastEntryTexts.GetValueOrDefault(le.Id)), le.CreatedAt);

            var cardNotes = notes.Where(n => n.CardNumber == card).ToList();
            var lastNote = cardNotes.OrderByDescending(n => n.CreatedAt).FirstOrDefault();

            var handover = handovers.FirstOrDefault(h => h.CardNumber == card);
            var cardPrs = prs.Where(p => p.CardNumber == card).ToList();

            result.Add(new HomeCardResponse
            {
                CardNumber = card,
                RepositoryId = cardPrs.FirstOrDefault()?.RepositoryId ?? register?.LegacyRepositoryId,
                OwnerUserId = ownerId,
                IsMine = ownerId == userId,
                Registered = register is not null,
                HasDescription = register?.HasDescription ?? false,
                HasRootCause = register?.HasRootCause ?? false,
                SummaryPublished = register?.SummaryPublished ?? false,
                MyRoles = myEvents.Select(e => e.Role).Distinct().ToList(),
                MyLastActivityAt = myEvents.Count > 0 ? myEvents.Max(e => e.At) : null,
                LastActivity = last is null ? null : new HomeCardActivity(last.Kind, last.Text,
                    IdFor(last.UserId, last.UserName), NameFor(last.UserId, last.UserName), last.At),
                Participants = participants.Take(MaxParticipants).ToList(),
                ParticipantsCount = participants.Count,
                PullRequests = cardPrs
                    .Where(p => p.Status != PullRequestGithubStatus.Legacy)
                    .Select(p => new HomeCardPullRequest(p.RepositoryId, p.GithubPrNumber, p.Url, p.Title, p.Status, p.IsDraft, p.UserId, p.CreatedAt))
                    .ToList(),
                Plan = planResponse,
                Timeline = new HomeCardTimeline { Count = cardEntries.Count, Last = lastEntry },
                Notes = new HomeCardNotes
                {
                    Count = cardNotes.Count,
                    Last = lastNote is null ? null
                        : new HomeCardEntry(IdFor(lastNote.AuthorUserId, lastNote.AuthorName), NameFor(lastNote.AuthorUserId, lastNote.AuthorName), Excerpt(lastNote.Text), lastNote.CreatedAt, lastNote.Number)
                },
                Handover = handover is null ? null : new HomeCardHandover(ParseGuid(handover.UpdatedBy) ?? ParseGuid(handover.CreatedBy), handover.At),
            });
        }

        return result;

        // Registro gravado só com o nome (Teams, ações do plano) → o usuário com esse nome, quando há um só.
        Guid? IdFor(Guid? id, string? name) =>
            id ?? (!string.IsNullOrWhiteSpace(name) && people.IdsByName.TryGetValue(name.Trim().ToLowerInvariant(), out var mapped) ? mapped : null);

        string NameFor(Guid? id, string? fallback) =>
            (IdFor(id, fallback) is { } resolved ? people.NamesById.GetValueOrDefault(resolved) : null) ?? fallback?.Trim() ?? string.Empty;
    }

    private sealed record People(Dictionary<Guid, string> NamesById, Dictionary<string, Guid> IdsByName);

    private async Task<People> ResolvePeopleAsync(List<CardEvent> events, CancellationToken ct)
    {
        var ids = events.Where(e => e.UserId is not null).Select(e => e.UserId!.Value).Distinct().ToList();
        var names = events.Where(e => e.UserId is null && !string.IsNullOrWhiteSpace(e.UserName))
            .Select(e => e.UserName!.Trim().ToLower())
            .Distinct()
            .ToList();

        var users = await auth.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) || names.Contains(u.FullName.ToLower()))
            .Select(u => new { u.Id, u.FullName })
            .ToListAsync(ct);

        var namesById = users.Where(u => !string.IsNullOrWhiteSpace(u.FullName))
            .ToDictionary(u => u.Id, u => u.FullName.Trim());
        // Nome repetido entre usuários não identifica ninguém: fica só o nome.
        var idsByName = users.Where(u => !string.IsNullOrWhiteSpace(u.FullName))
            .GroupBy(u => u.FullName.Trim().ToLowerInvariant())
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Id);
        return new People(namesById, idsByName);
    }

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;

    private static string PlanStatusLabel(string status) => status switch
    {
        ExecutionStatus.Pending => "pendente",
        ExecutionStatus.Running => "em andamento",
        ExecutionStatus.Paused => "pausado",
        ExecutionStatus.Completed => "concluído",
        ExecutionStatus.Failed => "falhou",
        ExecutionStatus.Cancelled => "cancelado",
        _ => status
    };

    /// <summary>Texto corrido para o cartão: sem markdown/HTML, numa linha, cortado.</summary>
    internal static string Excerpt(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        var text = CodeFence().Replace(markdown, " ");
        text = Image().Replace(text, " ");
        text = Link().Replace(text, "$1");
        text = Html().Replace(text, " ");
        text = MarkdownSymbols().Replace(text, " ");
        text = Spaces().Replace(text, " ").Trim();
        return text.Length <= ExcerptLength ? text : text[..ExcerptLength].TrimEnd() + "…";
    }

    [GeneratedRegex(@"```[\s\S]*?(```|$)")]
    private static partial Regex CodeFence();
    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();
    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Html();
    [GeneratedRegex(@"[#*`>|]+|^\s*[-+]\s+", RegexOptions.Multiline)]
    private static partial Regex MarkdownSymbols();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
