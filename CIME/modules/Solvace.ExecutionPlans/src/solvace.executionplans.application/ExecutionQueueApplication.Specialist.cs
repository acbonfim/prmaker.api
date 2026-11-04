using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// 0080: pista do Especialista do caso na fila — pedido <see cref="ExecutionRequestKind.Specialist"/>, um ativo por card,
/// fora do limite de execuções da máquina (até <see cref="SpecialistSlots"/>), só para executor que conhece a pista. A
/// sessão do Claude do especialista é do card (fica no <see cref="CaseChat"/>), não do plano.
/// </summary>
public partial class ExecutionQueueApplication
{
    /// <summary>"Fase" gravada no pedido do especialista (o modelo vem de SpecialistModel/SpecialistDeepModel).</summary>
    public const string SpecialistPhase = "specialist";

    public static bool SupportsSpecialist(ExecutionWorker worker) => !IsOlder(worker.AgentVersion, SpecialistAgentVersion);

    public async Task<ExecutionRequest?> EnqueueSpecialistAsync(string cardNumber, ExecutionActor requester,
        IReadOnlyList<(Guid UserId, string Name)> ownerCandidates, bool deep, CancellationToken cancellationToken)
    {
        var card = CaseChat.NormalizeCard(cardNumber);
        var now = DateTimeOffset.UtcNow;

        if (await _queue.GetActiveSpecialistForCardAsync(card, cancellationToken) is { } active)
        {
            if (active.Status == ExecutionRequestStatus.Queued)
            {
                // Ainda não começou: lê a mensagem nova junto (e passa a investigar a fundo, se pedido).
                if (deep) active.MarkDeep(now);
                active.RunNow(now);
                await SaveQuietlyAsync(cancellationToken);
                await WakeWorkersAsync(active.OwnerUserId, cancellationToken);
                return active;
            }
            await SetPendingRunAsync(card, true, now, cancellationToken);
            return active;
        }

        var candidates = ownerCandidates.Where(c => c.UserId != Guid.Empty).DistinctBy(c => c.UserId).ToList();
        if (candidates.Count == 0)
            throw new DomainException("Ninguém com executor do PRMake neste card — instale o executor em \"Meus executores\".");
        var withWorkers = await _queue.GetOwnersWithWorkersAsync(candidates.Select(c => c.UserId).ToList(), cancellationToken);
        var owner = candidates.FirstOrDefault(c => withWorkers.Contains(c.UserId));
        if (owner == default) owner = candidates[0];

        // A sessão do especialista vive numa máquina: retoma nela se estiver online; senão começa outra (com o contexto do caso).
        var chat = _cases is null ? null : await _cases.GetChatAsync(card, cancellationToken);
        ExecutionSession? session = null;
        Guid? target = null;
        if (chat?.SessionId is { Length: > 0 } sid)
        {
            var workers = (await _queue.GetWorkersByOwnerAsync(owner.UserId, cancellationToken)).Where(w => !w.IsRevoked).ToList();
            var home = chat.SessionHost is { } host
                ? workers.FirstOrDefault(w => string.Equals(w.Host, host, StringComparison.OrdinalIgnoreCase) && w.IsOnline(now))
                : null;
            if (home is not null)
            {
                session = new ExecutionSession { SessionId = sid, Host = chat.SessionHost, Cwd = chat.SessionCwd };
                target = home.Id;
            }
        }
        _queue.ClearTracking();

        var request = new ExecutionRequest(card, ExecutionRequestKind.Specialist, ExecutionRequestSource.Case, owner.UserId, owner.Name,
            requester.UserId, requester.Name, null, session, target, null, false, now);
        if (deep) request.MarkDeep(now);
        var saved = await AddRequestAsync(request, cancellationToken);
        await UpdateWaitReasonsAsync(owner.UserId, now, cancellationToken);
        return saved;
    }

    public async Task<ExecutionRequestResponse?> GetSpecialistRunAsync(string cardNumber, CancellationToken cancellationToken)
    {
        var card = CaseChat.NormalizeCard(cardNumber);
        var active = await _queue.GetActiveSpecialistForCardAsync(card, cancellationToken);
        if (active is not null)
            return await RespondAsync(active, cancellationToken);
        return (await _queue.GetLastSpecialistForCardAsync(card, cancellationToken))?.ToResponse();
    }

    public async Task<bool> IsSpecialistRequestActiveAsync(Guid requestId, string cardNumber, CancellationToken cancellationToken) =>
        await _queue.GetRequestAsync(requestId, cancellationToken) is { Kind: ExecutionRequestKind.Specialist } r
        && r.IsActive
        && string.Equals(r.CardNumber, cardNumber, StringComparison.Ordinal);

    private static string SpecialistPrompt(ExecutionRequest r, bool fresh)
    {
        var deep = r.Deep
            ? "\n\nPediram para INVESTIGAR A FUNDO: confirme no codigo/banco/arquivos antes de responder, sem pressa."
            : "\n\nResposta rapida: o que a pessoa precisa saber agora, com o minimo de leituras (aprofunde so se pedirem).";
        var start = fresh
            ? $"Voce e o Especialista do caso do card {r.CardNumber} no PRMake. Leia as instrucoes com " +
              "`cat ~/.claude/skills/especialista-do-caso/SKILL.md` (Bash) e siga-as — NAO use a ferramenta Skill. " +
              $"Comece por prmake_case_context (card {r.CardNumber}) e responda as mensagens novas (prmake_case_messages) com prmake_case_reply."
            : $"Mensagens novas na conversa do caso {r.CardNumber} ({r.RequestedBy} escreveu). Leia com prmake_case_messages (card {r.CardNumber}) " +
              "e responda com prmake_case_reply, conforme a skill especialista-do-caso (se nao lembrar das regras, " +
              "`cat ~/.claude/skills/especialista-do-caso/SKILL.md`).";
        return start +
               "\n\nVoce NAO altera o plano (o PRMake recusa): mudancas viram proposta (prmake_case_propose) que uma pessoa aceita; " +
               "acoes com efeito fora da conversa pedem confirmacao (prmake_case_action)." + deep +
               "\n\n(Executor do PRMake, sem terminal: responda e encerre a vez — a proxima mensagem retoma esta sessao.)";
    }

    private async Task RegisterCaseSessionAsync(ExecutionRequest r, Guid workerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_cases is null || string.IsNullOrEmpty(r.SessionId)) return;
        var host = (await _queue.GetWorkerAsync(workerId, cancellationToken))?.Host;
        await MutateChatAsync(r.CardNumber, chat => chat.RegisterSession(r.SessionId, host, r.SessionCwd, now), now, cancellationToken);
    }

    /// <summary>
    /// Fim de uma resposta do especialista: consumo na última mensagem dele e na conversa; falhou sem responder → aviso na
    /// conversa; chegou mensagem enquanto ele rodava → roda de novo.
    /// </summary>
    private async Task AfterSpecialistAsync(ExecutionRequest r, FinishExecutionRequestRequest finish, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_cases is null) return;
        try
        {
            _cases.ClearTracking();
            var written = await _cases.GetMessagesByRequestAsync(r.Id, cancellationToken);
            // O consumo fica na resposta (texto); só sem resposta, no último cartão (proposta/ação).
            if ((written.LastOrDefault(m => m.Kind == CaseMessageKind.Text) ?? written.LastOrDefault()) is { } last)
            {
                var ms = r.StartedAt is { } st && r.FinishedAt is { } ft ? (int?)Math.Max(0, (ft - st).TotalMilliseconds) : null;
                last.SetUsage(r.InputTokens, r.OutputTokens, ms, r.Model);
            }
            var chat = await _cases.GetChatAsync(r.CardNumber, cancellationToken);
            if (chat is null)
            {
                chat = new CaseChat(r.CardNumber, now);
                _cases.AddChat(chat);
            }
            chat.AddUsage(r.InputTokens, r.OutputTokens, r.CostUsd, now);
            var again = chat.PendingRun && r.Status != ExecutionRequestStatus.Cancelled;
            chat.SetPendingRun(false, now);

            CaseMessage? notice = null;
            if (written.Count == 0 && r.Status is ExecutionRequestStatus.Failed or ExecutionRequestStatus.Expired)
            {
                var number = await _cases.GetMaxMessageNumberAsync(r.CardNumber, cancellationToken) + 1;
                var why = r.LastError ?? r.FinishedReason ?? "o processo terminou com erro";
                notice = new CaseMessage(r.CardNumber, number, CaseAuthorKind.System, null, "PRMake", CaseMessageKind.System,
                    $"O especialista não conseguiu responder: {why}. Escreva de novo para tentar outra vez.", false, now);
                _cases.AddMessage(notice);
            }
            await _cases.SaveChangesAsync(cancellationToken);
            if (written.Count > 0 || notice is not null)
                await NotifyCaseAsync(r.CardNumber, new
                {
                    cardNumber = r.CardNumber, action = ExecutionPlanRealTimeEvents.CaseActions.Message, number = notice?.Number ?? written.Last().Number
                }, cancellationToken);

            if (again)
            {
                _queue.ClearTracking();
                await EnqueueSpecialistAsync(r.CardNumber, new ExecutionActor(r.RequestedByUserId, r.RequestedBy, false),
                    [(r.OwnerUserId, r.OwnerName)], r.Deep, cancellationToken);
            }
        }
        catch (Exception e) when (e is ExecutionPlanConcurrencyException or CaseNumberConflictException)
        {
            // best-effort: o consumo fica no pedido; a conversa segue.
            _cases.ClearTracking();
        }
    }

    private async Task SetPendingRunAsync(string card, bool pending, DateTimeOffset now, CancellationToken cancellationToken) =>
        await MutateChatAsync(card, chat => chat.SetPendingRun(pending, now), now, cancellationToken);

    private async Task MutateChatAsync(string card, Action<CaseChat> mutate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_cases is null) return;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            _cases.ClearTracking();
            var chat = await _cases.GetChatAsync(card, cancellationToken);
            if (chat is null)
            {
                chat = new CaseChat(card, now);
                _cases.AddChat(chat);
            }
            mutate(chat);
            try
            {
                await _cases.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (Exception e) when (e is ExecutionPlanConcurrencyException or CaseNumberConflictException)
            {
                await Task.Delay(Random.Shared.Next(10, 40) * attempt, cancellationToken);
            }
        }
    }

    private async Task SaveQuietlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _queue.SaveChangesAsync(cancellationToken);
        }
        catch (ExecutionPlanConcurrencyException)
        {
            // O executor pegou o pedido ao mesmo tempo — ele lê as mensagens novas de qualquer jeito.
            _queue.ClearTracking();
        }
    }

    private async Task NotifyCaseAsync(string card, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await _realTime.NotifyGroupAsync(ExecutionPlanRealTimeEvents.Group(card), ExecutionPlanRealTimeEvents.EventCaseUpdated, payload, cancellationToken);
        }
        catch
        {
            // best-effort
        }
    }
}
