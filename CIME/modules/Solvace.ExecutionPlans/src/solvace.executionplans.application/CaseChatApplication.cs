using System.Text;
using Cime.BuildingBlocks.RealTime;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>Quem escreve: uma pessoa (com permissão de decidir ou não) ou o especialista (token do caso).</summary>
public record CaseActor(Guid? UserId, string Name, bool IsManager = false, Guid? SpecialistRequestId = null)
{
    public bool IsSpecialist => SpecialistRequestId is not null;
    public ExecutionActor AsExecutionActor() => new(UserId, Name, false);
}

/// <summary>
/// 0080: Especialista do caso — a conversa do card entre as pessoas e um agente próprio (pista do especialista no
/// executor). A conversa nunca altera o plano: a proposta aceita por quem pode decidir vira uma instrução no plano atual
/// (o executor principal monta a nova versão); "Levar para o plano" faz o mesmo com uma mensagem.
/// </summary>
public class CaseChatApplication(
    ICaseChatRepository cases,
    IExecutionPlanRepository plans,
    IExecutionPlanApplication planApp,
    IExecutionQueueApplication queue,
    IRealTimeNotifier realTime,
    IPrmakeRulesProvider? rules = null,
    IWorkItemApplication? workItems = null)
{
    public const string SpecialistName = "Especialista do caso";
    private const int DefaultPageSize = 200;
    private const int ContextMessages = 30;
    private const int Retries = 6;

    // ── Tela ─────────────────────────────────────────────────────────────────────────────────────

    public async Task<CaseChatResponse> GetAsync(string cardNumber, CaseActor viewer, int? limit, CancellationToken cancellationToken)
    {
        var card = CaseChat.NormalizeCard(cardNumber);
        var size = Math.Clamp(limit ?? DefaultPageSize, 1, 500);
        var messages = await cases.GetMessagesAsync(card, null, size + 1, cancellationToken);
        var hasMore = messages.Count > size;
        if (hasMore) messages = messages.Skip(1).ToList();

        var files = await cases.GetFilesAsync(card, null, cancellationToken);
        var byMessage = files.Where(f => f.MessageId is not null).ToLookup(f => f.MessageId!.Value);
        var chat = await cases.GetChatAsync(card, cancellationToken);
        var currentPlan = await CurrentPlanAsync(card, cancellationToken);

        var notes = await plans.GetNotesByCardAsync(card, cancellationToken);
        var phases = notes.Count == 0 ? [] : (await plans.GetSummariesByCardAsync(card, cancellationToken)).ToDictionary(p => p.Id, p => p.Phase);
        var noteFiles = (notes.Count == 0 ? [] : await plans.GetNoteAttachmentsAsync(notes.Select(n => n.Id).ToList(), cancellationToken))
            .ToLookup(a => a.NoteId!.Value);

        return new CaseChatResponse
        {
            CardNumber = card,
            Messages = messages.Select(m => m.ToResponse(byMessage[m.Id])).ToList(),
            HasMore = hasMore,
            Points = (await cases.GetPointsAsync(card, cancellationToken)).Select(p => p.ToResponse()).ToList(),
            Proposals = (await cases.GetProposalsAsync(card, cancellationToken)).Select(p => p.ToResponse()).ToList(),
            Actions = (await cases.GetActionsAsync(card, cancellationToken)).Select(a => a.ToResponse()).ToList(),
            // 0085 r5 (1.7): o áudio da mensagem de voz toca na mensagem, mas não é arquivo da conversa.
            Files = CaseFile.Listed(files).Select(f => f.ToResponse()).ToList(),
            Participants = (await cases.GetParticipantsAsync(card, cancellationToken))
                .Select(p => new CaseParticipantResponse { UserId = p.UserId, Name = p.Name, Messages = p.Messages, LastAt = p.LastAt }).ToList(),
            PlanNotes = notes.Select(n => new CasePlanNoteResponse
            {
                Number = n.Number,
                PlanPhase = phases.GetValueOrDefault(n.PlanId, ExecutionPhase.Analysis),
                StepKey = n.StepKey,
                Text = n.Text,
                AuthorUserId = n.AuthorUserId,
                AuthorName = n.FromExecutor ? "Claude" : n.AuthorName,
                FromExecutor = n.FromExecutor,
                CreatedAt = n.CreatedAt,
                Attachments = noteFiles[n.Id].Select(a => a.Name).ToList()
            }).ToList(),
            Knows = await KnowsAsync(card, cancellationToken),
            Run = await queue.GetSpecialistRunAsync(card, cancellationToken),
            SessionHost = chat?.SessionHost,
            InputTokens = chat?.InputTokens ?? 0,
            OutputTokens = chat?.OutputTokens ?? 0,
            CostUsd = chat?.CostUsd ?? 0,
            CanDecide = CanDecide(viewer, currentPlan),
            HasPlan = currentPlan is not null,
            ServerTime = DateTimeOffset.UtcNow
        };
    }

    /// <summary>Mensagem de uma pessoa: grava, avisa a tela e, se for para o especialista, pede a resposta dele.</summary>
    public async Task<CaseMessageResponse> PostAsync(string cardNumber, PostCaseMessageRequest request, IReadOnlyList<ExecutionArtifactUpload> uploads,
        CaseActor actor, CancellationToken cancellationToken)
    {
        if (actor.IsSpecialist)
            throw new ExecutionForbiddenException("O especialista responde com prmake_case_reply.");
        var card = CaseChat.NormalizeCard(cardNumber);
        ValidateUploads(uploads);
        var now = DateTimeOffset.UtcNow;
        var kind = request.Voice ? CaseMessageKind.Voice : CaseMessageKind.Text;
        if (request.Voice && !uploads.Any(u => CaseFile.KindOf(u.ContentType ?? string.Empty, u.FileName) == "audio"))
            throw new DomainException("A mensagem de voz precisa do áudio.");

        var (message, files) = await AddMessageAsync(card, now, number =>
        {
            var m = new CaseMessage(card, number, CaseAuthorKind.Person, actor.UserId, actor.Name, kind, request.Text, uploads.Count > 0, now);
            if (request.Voice) m.SetVoice(request.Transcript);
            m.SetFlags(request.PeopleOnly, request.Deep);
            m.SetReplyTo(request.ReplyTo);
            return m;
        }, uploads, actor.Name, fromSpecialist: false, cancellationToken);

        await NotifyAsync(card, new { cardNumber = card, action = ExecutionPlanRealTimeEvents.CaseActions.Message, number = message.Number, userId = actor.UserId },
            cancellationToken);

        var people = (await cases.GetParticipantsAsync(card, cancellationToken)).Count;
        if (message.AsksSpecialist(people))
            await AskSpecialistAsync(card, actor, message.Deep, cancellationToken);
        return message.ToResponse(files);
    }

    public async Task<CaseMessageResponse> EditAsync(string cardNumber, Guid messageId, string? text, CaseActor actor, CancellationToken cancellationToken)
    {
        var card = CaseChat.NormalizeCard(cardNumber);
        var message = await cases.GetMessageAsync(card, messageId, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Mensagem não encontrada.");
        if (message.IsDeleted || !message.CanBeChangedBy(actor.UserId))
            throw new ExecutionForbiddenException("Só quem escreveu a mensagem pode editá-la.");
        message.Edit(text, DateTimeOffset.UtcNow);
        await cases.SaveChangesAsync(cancellationToken);
        await NotifyAsync(card, new { cardNumber = card, action = ExecutionPlanRealTimeEvents.CaseActions.Edited, number = message.Number }, cancellationToken);
        return message.ToResponse(await cases.GetFilesAsync(card, [message.Id], cancellationToken));
    }

    public async Task DeleteAsync(string cardNumber, Guid messageId, CaseActor actor, CancellationToken cancellationToken)
    {
        var card = CaseChat.NormalizeCard(cardNumber);
        var message = await cases.GetMessageAsync(card, messageId, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Mensagem não encontrada.");
        if (!message.CanBeChangedBy(actor.UserId))
            throw new ExecutionForbiddenException("Só quem escreveu a mensagem pode apagá-la.");
        message.Delete(DateTimeOffset.UtcNow);
        await cases.SaveChangesAsync(cancellationToken);
        await NotifyAsync(card, new { cardNumber = card, action = ExecutionPlanRealTimeEvents.CaseActions.Deleted, number = message.Number }, cancellationToken);
    }

    /// <summary>"Fulano está digitando" — só tempo real, nada gravado.</summary>
    public Task TypingAsync(string cardNumber, CaseActor actor, CancellationToken cancellationToken) =>
        NotifyAsync(CaseChat.NormalizeCard(cardNumber),
            new { cardNumber = CaseChat.NormalizeCard(cardNumber), action = ExecutionPlanRealTimeEvents.CaseActions.Typing, by = actor.Name, userId = actor.UserId },
            cancellationToken);

    /// <summary>Pede ao especialista para responder agora (ex.: "Tentar de novo", atalhos sem texto).</summary>
    public async Task<ExecutionRequestResponse?> AskAsync(string cardNumber, bool deep, CaseActor actor, CancellationToken cancellationToken)
    {
        var card = CaseChat.NormalizeCard(cardNumber);
        await AskSpecialistAsync(card, actor, deep, cancellationToken);
        return await queue.GetSpecialistRunAsync(card, cancellationToken);
    }

    public async Task<(CaseFile File, byte[] Data)> GetFileAsync(string cardNumber, Guid fileId, CancellationToken cancellationToken)
    {
        var card = CaseChat.NormalizeCard(cardNumber);
        var file = await cases.GetFileAsync(card, fileId, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Arquivo não encontrado.");
        var data = await cases.GetFileContentAsync(file.Id, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Arquivo sem conteúdo.");
        return (file, data);
    }

    public async Task<(CaseFile File, byte[] Data)> GetFileByNumberAsync(string cardNumber, int number, CancellationToken cancellationToken)
    {
        var card = CaseChat.NormalizeCard(cardNumber);
        var file = await cases.GetFileByNumberAsync(card, number, cancellationToken) ?? throw new ExecutionPlanNotFoundException($"Arquivo #{number} não encontrado.");
        var data = await cases.GetFileContentAsync(file.Id, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Arquivo sem conteúdo.");
        return (file, data);
    }

    /// <summary>"Levar para o plano": a mensagem vira uma instrução no plano atual (decisão explícita da pessoa).</summary>
    public async Task<ExecutionNoteResponse> ToPlanAsync(string cardNumber, Guid messageId, string? extra, CaseActor actor, CancellationToken cancellationToken)
    {
        if (actor.IsSpecialist)
            throw new ExecutionForbiddenException("O especialista não leva nada para o plano: proponha (prmake_case_propose).");
        var card = CaseChat.NormalizeCard(cardNumber);
        var message = await cases.GetMessageAsync(card, messageId, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Mensagem não encontrada.");
        var plan = await CurrentPlanAsync(card, cancellationToken) ?? throw new DomainException("O card ainda não tem plano — não há onde registrar.");
        var content = message.Kind == CaseMessageKind.Voice && message.Transcript is { } t ? t : message.Text;
        var text = new StringBuilder()
            .AppendLine($"**Da conversa com o Especialista do caso** (mensagem {message.Number} de {message.AuthorName}, levada para o plano por {actor.Name}):")
            .AppendLine()
            .AppendLine(string.Join("\n", content.Split('\n').Select(l => "> " + l)));
        if (!string.IsNullOrWhiteSpace(extra))
            text.AppendLine().AppendLine(extra.Trim());
        var note = await planApp.AddNoteAsync(plan.Id, text.ToString(), null, [], actor.AsExecutionActor(), cancellationToken);
        await RequestWhenPlanStoppedAsync(card, plan, ExecutionRequestSource.CaseProposal, text.ToString(), actor, cancellationToken);
        await SystemMessageAsync(card, $"{actor.Name} levou a mensagem {message.Number} para o plano (comentário #{note.Number}).", cancellationToken);
        return note;
    }

    // ── Pontos combinados ────────────────────────────────────────────────────────────────────────

    public async Task<CasePointResponse> AddPointAsync(string cardNumber, CasePointRequest request, CaseActor actor, CancellationToken cancellationToken)
    {
        var card = CaseChat.NormalizeCard(cardNumber);
        var point = new CasePoint(card, request.Text ?? string.Empty, request.Status, request.MessageNumber,
            actor.IsSpecialist ? SpecialistName : actor.Name, actor.IsSpecialist, DateTimeOffset.UtcNow);
        cases.AddPoint(point);
        await cases.SaveChangesAsync(cancellationToken);
        await NotifyAsync(card, new { cardNumber = card, action = ExecutionPlanRealTimeEvents.CaseActions.Point }, cancellationToken);
        return point.ToResponse();
    }

    public async Task<CasePointResponse> UpdatePointAsync(string cardNumber, Guid pointId, CasePointRequest request, CaseActor actor, CancellationToken cancellationToken)
    {
        var card = CaseChat.NormalizeCard(cardNumber);
        var point = await cases.GetPointAsync(card, pointId, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Ponto não encontrado.");
        point.Update(request.Text, request.Status, DateTimeOffset.UtcNow);
        await cases.SaveChangesAsync(cancellationToken);
        await NotifyAsync(card, new { cardNumber = card, action = ExecutionPlanRealTimeEvents.CaseActions.Point }, cancellationToken);
        return point.ToResponse();
    }

    // ── Propostas ────────────────────────────────────────────────────────────────────────────────

    /// <summary>O especialista propõe uma nova versão do plano: cartão na conversa; as pendentes anteriores saem.</summary>
    public async Task<CaseProposalResponse> ProposeAsync(string cardNumber, CaseProposalRequest request, CaseActor actor, CancellationToken cancellationToken)
    {
        RequireSpecialist(actor);
        var card = CaseChat.NormalizeCard(cardNumber);
        var now = DateTimeOffset.UtcNow;
        CaseProposal? proposal = null;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                cases.ClearTracking();
                foreach (var pending in (await cases.GetProposalsAsync(card, cancellationToken)).Where(p => p.IsPending))
                    pending.Supersede(now);
                proposal = new CaseProposal(card, await cases.GetMaxProposalNumberAsync(card, cancellationToken) + 1, request.Title ?? string.Empty,
                    request.Summary, request.Phase, request.Changes ?? [], now);
                cases.AddProposal(proposal);
                var message = await NewSpecialistMessageAsync(card, CaseMessageKind.Proposal,
                    string.IsNullOrWhiteSpace(request.Message) ? $"Chegamos a uma conclusão. Quer que eu gere a nova versão do plano? **{proposal.Title}**" : request.Message,
                    actor, now, cancellationToken);
                message.LinkProposal(proposal.Id);
                proposal.LinkMessage(message.Id);
                await cases.SaveChangesAsync(cancellationToken);
                await NotifyAsync(card, new { cardNumber = card, action = ExecutionPlanRealTimeEvents.CaseActions.Proposal, number = message.Number }, cancellationToken);
                return proposal.ToResponse();
            }
            catch (CaseNumberConflictException) when (attempt < Retries)
            {
                await Task.Delay(Random.Shared.Next(10, 50) * attempt, cancellationToken);
            }
        }
    }

    /// <summary>Aceitar (vira instrução no plano atual) ou recusar — só quem pode decidir.</summary>
    public async Task<CaseProposalResponse> DecideProposalAsync(string cardNumber, Guid proposalId, CaseProposalDecisionRequest request, CaseActor actor,
        CancellationToken cancellationToken)
    {
        if (actor.IsSpecialist)
            throw new ExecutionForbiddenException("Quem decide a proposta é uma pessoa.");
        var card = CaseChat.NormalizeCard(cardNumber);
        var plan = await CurrentPlanAsync(card, cancellationToken);
        if (request.Accept && !CanDecide(actor, plan))
            throw new ExecutionForbiddenException("Só o dono do plano, um admin ou um gestor aceita a proposta — comente na conversa.");
        var proposal = await cases.GetProposalAsync(card, proposalId, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Proposta não encontrada.");
        var now = DateTimeOffset.UtcNow;
        if (!request.Accept)
        {
            proposal.Reject(request.Reason, actor.Name, now);
            await cases.SaveChangesAsync(cancellationToken);
            await SystemMessageAsync(card, $"{actor.Name} recusou a proposta #{proposal.Number}" +
                                           (string.IsNullOrWhiteSpace(request.Reason) ? "." : $": {request.Reason.Trim()}"), cancellationToken);
            return proposal.ToResponse();
        }

        if (plan is null)
            throw new DomainException("O card ainda não tem plano — peça a análise antes de aceitar a proposta.");
        proposal.Accept(request.Execute, actor.Name, now);
        await cases.SaveChangesAsync(cancellationToken);
        var instruction = proposal.InstructionText(actor.Name);
        var note = await planApp.AddNoteAsync(plan.Id, instruction, null, [], actor.AsExecutionActor(), cancellationToken);
        cases.ClearTracking();
        var saved = await cases.GetProposalAsync(card, proposalId, cancellationToken) ?? proposal;
        saved.SetPlanNote(note.Number);
        await cases.SaveChangesAsync(cancellationToken);
        await RequestWhenPlanStoppedAsync(card, plan, ExecutionRequestSource.CaseProposal, instruction, actor, cancellationToken);
        await SystemMessageAsync(card,
            $"{actor.Name} aceitou a proposta #{saved.Number}{(saved.Execute ? " para já executar" : string.Empty)} — virou o comentário #{note.Number} do plano e o " +
            "Claude do tratamento monta a nova versão.", cancellationToken);
        return saved.ToResponse();
    }

    // ── Ações com confirmação ────────────────────────────────────────────────────────────────────

    public async Task<CaseActionResponse> RequestActionAsync(string cardNumber, CaseActionRequest request, CaseActor actor, CancellationToken cancellationToken)
    {
        RequireSpecialist(actor);
        var card = CaseChat.NormalizeCard(cardNumber);
        var now = DateTimeOffset.UtcNow;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                cases.ClearTracking();
                var action = new CaseAction(card, await cases.GetMaxActionNumberAsync(card, cancellationToken) + 1, request.Title ?? string.Empty, request.Detail, now);
                cases.AddAction(action);
                var message = await NewSpecialistMessageAsync(card, CaseMessageKind.Action,
                    string.IsNullOrWhiteSpace(request.Message) ? $"Posso fazer isto? **{action.Title}**" : request.Message, actor, now, cancellationToken);
                message.LinkAction(action.Id);
                action.LinkMessage(message.Id);
                await cases.SaveChangesAsync(cancellationToken);
                await NotifyAsync(card, new { cardNumber = card, action = ExecutionPlanRealTimeEvents.CaseActions.Action, number = message.Number }, cancellationToken);
                return action.ToResponse();
            }
            catch (CaseNumberConflictException) when (attempt < Retries)
            {
                await Task.Delay(Random.Shared.Next(10, 50) * attempt, cancellationToken);
            }
        }
    }

    public async Task<CaseActionResponse> DecideActionAsync(string cardNumber, Guid actionId, bool confirm, CaseActor actor, CancellationToken cancellationToken)
    {
        if (actor.IsSpecialist)
            throw new ExecutionForbiddenException("Quem confirma a ação é uma pessoa.");
        var card = CaseChat.NormalizeCard(cardNumber);
        var action = await cases.GetActionAsync(card, actionId, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Ação não encontrada.");
        action.Decide(confirm, actor.Name, DateTimeOffset.UtcNow);
        await cases.SaveChangesAsync(cancellationToken);
        await SystemMessageAsync(card, confirm
            ? $"{actor.Name} confirmou a ação #{action.Number}: {action.Title}. O especialista pode executar."
            : $"{actor.Name} não autorizou a ação #{action.Number}: {action.Title}.", cancellationToken);
        if (confirm)
            await AskSpecialistAsync(card, actor, false, cancellationToken);
        return action.ToResponse();
    }

    public async Task<CaseActionResponse> FinishActionAsync(string cardNumber, Guid actionId, CaseActionFinishRequest request, CaseActor actor,
        CancellationToken cancellationToken)
    {
        RequireSpecialist(actor);
        var card = CaseChat.NormalizeCard(cardNumber);
        var action = await cases.GetActionAsync(card, actionId, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Ação não encontrada.");
        action.Finish(request.Ok, request.Result, DateTimeOffset.UtcNow);
        await cases.SaveChangesAsync(cancellationToken);
        await NotifyAsync(card, new { cardNumber = card, action = ExecutionPlanRealTimeEvents.CaseActions.Action }, cancellationToken);
        return action.ToResponse();
    }

    /// <summary>Há ação confirmada e ainda não concluída (o especialista pode executá-la).</summary>
    public async Task<CaseAction?> ConfirmedActionAsync(string cardNumber, Guid actionId, CancellationToken cancellationToken) =>
        await cases.GetActionAsync(CaseChat.NormalizeCard(cardNumber), actionId, cancellationToken) is { Status: CaseActionStatus.Confirmed } a ? a : null;

    // ── Especialista ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Mensagens novas para o especialista (depois da última que ele leu, ou de <paramref name="after"/>) — marca como lidas.</summary>
    public async Task<List<CaseMessageResponse>> MessagesForSpecialistAsync(string cardNumber, int? after, CaseActor actor, CancellationToken cancellationToken)
    {
        var card = CaseChat.NormalizeCard(cardNumber);
        var chat = await cases.GetChatAsync(card, cancellationToken);
        var from = after ?? chat?.LastReadNumber ?? 0;
        var messages = await cases.GetMessagesAsync(card, from, 100, cancellationToken);
        var files = (await cases.GetFilesAsync(card, messages.Select(m => m.Id).ToList(), cancellationToken)).ToLookup(f => f.MessageId!.Value);
        if (actor.IsSpecialist && messages.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            if (chat is null)
            {
                chat = new CaseChat(card, now);
                cases.AddChat(chat);
            }
            chat.MarkRead(messages.Max(m => m.Number), now);
            try
            {
                await cases.SaveChangesAsync(cancellationToken);
            }
            catch (Exception e) when (e is ExecutionPlanConcurrencyException or CaseNumberConflictException)
            {
                cases.ClearTracking();
            }
        }
        // As dele mesmo (respostas, propostas, ações) não são "novas" para ele; marcar lido só no que ele leu evita pular
        // a mensagem que alguém mandou enquanto ele escrevia.
        // 0085 r5 (1.7): o áudio de quem fala não vai para o especialista — ele lê a transcrição.
        return messages.Where(m => !(actor.IsSpecialist && m.FromSpecialist)).Select(m => m.ToResponse(CaseFile.Listed(files[m.Id]))).ToList();
    }

    /// <summary>Resposta do especialista (com os passos que ele fez e os arquivos que gerou).</summary>
    public async Task<CaseMessageResponse> ReplyAsync(string cardNumber, CaseReplyRequest request, CaseActor actor, string? model, CancellationToken cancellationToken)
    {
        RequireSpecialist(actor);
        var card = CaseChat.NormalizeCard(cardNumber);
        var now = DateTimeOffset.UtcNow;
        var fileNumbers = (request.Files ?? []).Distinct().ToList();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                cases.ClearTracking();
                var attached = new List<CaseFile>();
                foreach (var n in fileNumbers)
                    attached.Add(await cases.GetFileByNumberAsync(card, n, cancellationToken) ?? throw new DomainException($"Arquivo #{n} não encontrado."));
                var number = await cases.GetMaxMessageNumberAsync(card, cancellationToken) + 1;
                var message = new CaseMessage(card, number, CaseAuthorKind.Specialist, actor.UserId, SpecialistName, CaseMessageKind.Text,
                    request.Text, attached.Count > 0, now);
                message.SetSteps(request.Steps);
                message.SetReplyTo(request.ReplyTo);
                message.SetRequest(actor.SpecialistRequestId, model);
                cases.AddMessage(message);
                await cases.SaveChangesAsync(cancellationToken);
                if (attached.Count > 0)
                {
                    foreach (var f in attached)
                        f.AttachTo(message.Id); // valida (arquivo de outra mensagem não muda de lugar)
                    await cases.AttachFilesAsync(attached.Select(f => f.Id).ToList(), message.Id, cancellationToken);
                }
                await NotifyAsync(card, new { cardNumber = card, action = ExecutionPlanRealTimeEvents.CaseActions.Message, number }, cancellationToken);
                return message.ToResponse(await cases.GetFilesAsync(card, [message.Id], cancellationToken));
            }
            catch (CaseNumberConflictException) when (attempt < Retries)
            {
                await Task.Delay(Random.Shared.Next(10, 50) * attempt, cancellationToken);
            }
        }
    }

    /// <summary>Arquivo gerado pelo especialista (imagem, script, CSV): fica na conversa e entra numa resposta pelo número.</summary>
    public async Task<CaseFileResponse> UploadAsync(string cardNumber, ExecutionArtifactUpload upload, CaseActor actor, CancellationToken cancellationToken)
    {
        var card = CaseChat.NormalizeCard(cardNumber);
        ValidateUploads([upload]);
        var now = DateTimeOffset.UtcNow;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                cases.ClearTracking();
                if (await cases.GetFilesSizeAsync(card, cancellationToken) + upload.Data.LongLength > CaseFile.MaxCardBytes)
                    throw new DomainException($"Os arquivos da conversa passariam do limite de {CaseFile.MaxCardBytes / (1024 * 1024)} MB.");
                var file = new CaseFile(card, null, await cases.GetMaxFileNumberAsync(card, cancellationToken) + 1, upload.FileName, upload.ContentType,
                    upload.Data, actor.IsSpecialist ? SpecialistName : actor.Name, actor.IsSpecialist, now);
                cases.AddFile(file);
                await cases.SaveChangesAsync(cancellationToken);
                return file.ToResponse();
            }
            catch (CaseNumberConflictException) when (attempt < Retries)
            {
                await Task.Delay(Random.Shared.Next(10, 50) * attempt, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Contexto compacto do caso para o especialista: planos e versões (com motivo do cancelamento), decisões, perguntas
    /// abertas, divergência, PRs, pontos combinados, propostas, ações e as últimas mensagens — sem o texto grande das
    /// análises (ele busca sob demanda).
    /// </summary>
    public async Task<string> ContextAsync(string cardNumber, CancellationToken cancellationToken)
    {
        var card = CaseChat.NormalizeCard(cardNumber);
        var sb = new StringBuilder();
        sb.AppendLine($"# Caso {card}");
        var summaries = await planApp.GetByCardAsync(card, cancellationToken);
        if (summaries.Count == 0)
            sb.AppendLine().AppendLine("O card ainda não tem plano de execução (nenhuma análise registrada no PRMake).");

        foreach (var phase in new[] { ExecutionPhase.Analysis, ExecutionPhase.Correction })
        {
            var versions = summaries.Where(s => s.Phase == phase).OrderBy(s => s.VersionNumber).ToList();
            if (versions.Count == 0) continue;
            sb.AppendLine().AppendLine(phase == ExecutionPhase.Analysis ? "## Análise" : "## Correção");
            foreach (var v in versions)
            {
                var reason = string.IsNullOrWhiteSpace(v.StatusReason) ? string.Empty : $" — motivo: \"{v.StatusReason}\"";
                var by = string.IsNullOrWhiteSpace(v.StatusChangedBy) ? string.Empty : $" por {v.StatusChangedBy}";
                sb.AppendLine($"- v{v.VersionNumber} ({v.Status}{by}{reason}): {v.Title} · {v.StepsCompleted}/{v.StepsTotal} etapas · criada {v.CreatedAt:dd/MM HH:mm} UTC");
            }
            var latest = await planApp.GetAsync(versions[^1].Id, cancellationToken);
            sb.AppendLine($"### Versão atual (v{versions[^1].VersionNumber})");
            foreach (var st in latest.Steps.OrderBy(s => s.Order))
            {
                var r = string.IsNullOrWhiteSpace(st.StatusReason) ? string.Empty : $" — {Cut(st.StatusReason, 200)}";
                sb.AppendLine($"- [{st.Status}] {st.Key}: {st.Title}{(st.Executor == ExecutionExecutor.User ? " (pessoa)" : string.Empty)}{r}");
            }
            var answered = latest.Questions.Where(q => q.Status == ExecutionQuestionStatus.Answered && q.ReplacedBy is null).ToList();
            if (answered.Count > 0)
            {
                sb.AppendLine("Decisões (perguntas respondidas):");
                foreach (var q in answered)
                    sb.AppendLine($"- {Cut(Plain(q.Text), 220)} → **{q.Answer}** ({q.AnsweredBy})");
            }
            var open = latest.Questions.Where(q => q.Status == ExecutionQuestionStatus.Open).ToList();
            if (open.Count > 0)
            {
                sb.AppendLine("Perguntas abertas (esperando a pessoa):");
                foreach (var q in open)
                    sb.AppendLine($"- {Cut(Plain(q.Text), 300)}");
            }
            foreach (var l in latest.Links.Where(l => l.Kind == "pr"))
                sb.AppendLine($"- PR: {l.Title ?? l.Url} ({l.Status ?? "?"}{(l.ChecksStatus is { } ci ? ", CI " + ci : string.Empty)}) {l.Url}");
            if (latest.Divergence is { } d)
                sb.AppendLine($"Divergência{(d.Pending ? " PENDENTE" : $" confirmada por {d.ConfirmedBy}")}: {Cut(d.Text, 600)}");
            if (!string.IsNullOrWhiteSpace(latest.Summary))
                sb.AppendLine($"Resumo do plano: {Cut(latest.Summary, 1500)}");
            var files = latest.Artifacts.Where(a => a.NoteId is null).Select(a => $"{a.Name} ({a.Kind})").Take(30).ToList();
            if (files.Count > 0)
                sb.AppendLine($"Arquivos do plano: {string.Join(", ", files)} — leia com prmake_attachment se precisar.");
        }

        // 0085: regras do card (o especialista não inventa nome de branch) e itens de trabalho (não repete item).
        if (rules is not null && await rules.RulesMarkdownAsync(card, summary: true, cancellationToken) is { Length: > 0 } rulesText)
        {
            sb.AppendLine().AppendLine("## Regras do PRMake (resumo)");
            sb.Append(rulesText);
            sb.AppendLine("Nunca invente nome de branch, base ou destino: use estas regras (completas em prmake_rules).");
        }
        if (workItems is not null)
        {
            sb.AppendLine().AppendLine("## Itens de trabalho");
            string? itemsText = null;
            try
            {
                itemsText = await workItems.SummaryMarkdownAsync(card, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                itemsText = $"(não consegui listar os itens: {e.Message} — use prmake_work_items)";
            }
            sb.AppendLine(string.IsNullOrWhiteSpace(itemsText) ? "Nenhum item de trabalho no card." : itemsText.TrimEnd());
            sb.AppendLine("Antes de propor ou criar um item, confira se já existe item com o mesmo objetivo (fingerprint/status) — " +
                          "pendente igual: ajuste com prmake_work_item_update; iniciado/concluído: não repita. Duplicata só com " +
                          "allowDuplicate + motivo, quando for de fato outro trabalho.");
        }

        var points = await cases.GetPointsAsync(card, cancellationToken);
        if (points.Count > 0)
        {
            sb.AppendLine().AppendLine("## Pontos combinados na conversa");
            foreach (var p in points.Where(p => p.Status != CasePointStatus.Dropped))
                sb.AppendLine($"- [{p.Status}] {p.Text}");
        }
        var proposals = await cases.GetProposalsAsync(card, cancellationToken);
        if (proposals.Count > 0)
        {
            sb.AppendLine().AppendLine("## Propostas");
            foreach (var p in proposals.TakeLast(5))
                sb.AppendLine($"- #{p.Number} [{p.Status}{(p.DecidedBy is null ? string.Empty : " por " + p.DecidedBy)}] {p.Title}{(p.RejectReason is null ? string.Empty : " — " + p.RejectReason)}");
        }
        var actions = await cases.GetActionsAsync(card, cancellationToken);
        if (actions.Count > 0)
        {
            sb.AppendLine().AppendLine("## Ações");
            foreach (var a in actions.TakeLast(10))
                sb.AppendLine($"- #{a.Number} [{a.Status}] {a.Title} (id {a.Id}){(a.Result is null ? string.Empty : " — " + Cut(a.Result, 200))}");
        }

        var notes = await plans.GetNotesByCardAsync(card, cancellationToken);
        if (notes.Count > 0)
        {
            sb.AppendLine().AppendLine("## Comentários antigos dos planos (histórico)");
            foreach (var n in notes.TakeLast(8))
                sb.AppendLine($"- #{n.Number} {(n.FromExecutor ? "Claude" : n.AuthorName)} {n.CreatedAt:dd/MM HH:mm}: {Cut(Plain(n.Text), 300)}");
        }

        var messages = await cases.GetMessagesAsync(card, null, ContextMessages, cancellationToken);
        if (messages.Count > 0)
        {
            sb.AppendLine().AppendLine($"## Conversa (últimas {messages.Count} mensagens)");
            var files = (await cases.GetFilesAsync(card, messages.Select(m => m.Id).ToList(), cancellationToken)).ToLookup(f => f.MessageId!.Value);
            foreach (var m in messages)
                sb.AppendLine(MessageLine(m, files[m.Id]));
        }
        sb.AppendLine().AppendLine("Arquivos da conversa: prmake-case.sh file <card> <n> baixa o arquivo #n (imagem/áudio/documento) para ler.");
        return sb.ToString();
    }

    public static string MessageLine(CaseMessage m, IEnumerable<CaseFile> files)
    {
        var who = m.AuthorKind switch
        {
            CaseAuthorKind.Specialist => "Especialista",
            CaseAuthorKind.System => "PRMake",
            _ => m.AuthorName
        };
        var content = m.Kind == CaseMessageKind.Voice
            ? $"[mensagem de voz] {m.Transcript ?? "(sem transcrição — peça um resumo em texto)"}"
            : m.Text;
        var flags = (m.PeopleOnly ? " [só entre pessoas]" : string.Empty) + (m.Deep ? " [investigar a fundo]" : string.Empty);
        // 0085 r5 (1.7): áudio de quem fala fica fora (a mensagem de voz já traz a transcrição).
        var att = string.Join(", ", CaseFile.Listed(files).Select(f => $"arquivo #{f.Number} {f.Name} ({f.Kind})"));
        return $"- ({m.Number}) {who} {m.CreatedAt:dd/MM HH:mm}{flags}: {Cut(content, 1500)}{(att.Length > 0 ? " — " + att : string.Empty)}";
    }

    // ── Internos ─────────────────────────────────────────────────────────────────────────────────

    private async Task AskSpecialistAsync(string card, CaseActor actor, bool deep, CancellationToken cancellationToken)
    {
        // Máquina: de quem escreveu (se tiver executor), senão do dono do plano atual.
        var candidates = new List<(Guid, string)>();
        if (actor.UserId is { } me) candidates.Add((me, actor.Name));
        if (await CurrentPlanAsync(card, cancellationToken) is { CreatedByUserId: { } owner } plan)
            candidates.Add((owner, plan.CreatedBy));
        if (candidates.Count == 0)
            return;
        try
        {
            await queue.EnqueueSpecialistAsync(card, actor.AsExecutionActor(), candidates, deep, cancellationToken);
        }
        catch (DomainException e)
        {
            await SystemMessageAsync(card, $"O especialista não pôde ser chamado: {e.Message}", cancellationToken);
        }
    }

    private async Task<(CaseMessage Message, List<CaseFile> Files)> AddMessageAsync(string card, DateTimeOffset now, Func<int, CaseMessage> create,
        IReadOnlyList<ExecutionArtifactUpload> uploads, string createdBy, bool fromSpecialist, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                cases.ClearTracking();
                if (uploads.Count > 0 && await cases.GetFilesSizeAsync(card, cancellationToken) + uploads.Sum(u => u.Data.LongLength) > CaseFile.MaxCardBytes)
                    throw new DomainException($"Os arquivos da conversa passariam do limite de {CaseFile.MaxCardBytes / (1024 * 1024)} MB.");
                var message = create(await cases.GetMaxMessageNumberAsync(card, cancellationToken) + 1);
                cases.AddMessage(message);
                var files = new List<CaseFile>();
                var fileNumber = await cases.GetMaxFileNumberAsync(card, cancellationToken);
                foreach (var u in uploads)
                {
                    var file = new CaseFile(card, message.Id, ++fileNumber, u.FileName, u.ContentType, u.Data, createdBy, fromSpecialist, now);
                    cases.AddFile(file);
                    files.Add(file);
                }
                if (await cases.GetChatAsync(card, cancellationToken) is null)
                    cases.AddChat(new CaseChat(card, now));
                await cases.SaveChangesAsync(cancellationToken);
                return (message, files);
            }
            catch (CaseNumberConflictException) when (attempt < Retries)
            {
                await Task.Delay(Random.Shared.Next(10, 50) * attempt, cancellationToken);
            }
        }
    }

    private async Task<CaseMessage> NewSpecialistMessageAsync(string card, string kind, string text, CaseActor actor, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var message = new CaseMessage(card, await cases.GetMaxMessageNumberAsync(card, cancellationToken) + 1, CaseAuthorKind.Specialist, actor.UserId,
            SpecialistName, kind, text, false, now);
        message.SetRequest(actor.SpecialistRequestId, null);
        cases.AddMessage(message);
        return message;
    }

    private async Task SystemMessageAsync(string card, string text, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var (message, _) = await AddMessageAsync(card, now,
            number => new CaseMessage(card, number, CaseAuthorKind.System, null, "PRMake", CaseMessageKind.System, text, false, now),
            [], "PRMake", false, cancellationToken);
        await NotifyAsync(card, new { cardNumber = card, action = ExecutionPlanRealTimeEvents.CaseActions.Message, number = message.Number }, cancellationToken);
    }

    private async Task<ExecutionPlan?> CurrentPlanAsync(string card, CancellationToken cancellationToken) =>
        await plans.GetCurrentPlanIdAsync(card, cancellationToken) is { } id ? await plans.GetPlanWithStepsAsync(id, cancellationToken) : null;

    /// <summary>
    /// O comentário só retoma plano ativo: com o plano cancelado, concluído ou com falha ninguém chamava o executor e a
    /// proposta aceita ficava parada (card 77900). Aí o pedido vai direto para a fila — nova análise (ou retomada, na
    /// falha) com a instrução no prompt.
    /// </summary>
    private async Task RequestWhenPlanStoppedAsync(string card, ExecutionPlan plan, string source, string instruction, CaseActor actor,
        CancellationToken cancellationToken)
    {
        if (!plan.IsFinished && plan.Status != ExecutionStatus.Failed)
            return;
        try
        {
            await queue.CreateAsync(new CreateExecutionRequestRequest { CardNumber = card, Note = instruction }, actor.AsExecutionActor(), source,
                cancellationToken);
        }
        catch (DomainException e)
        {
            await SystemMessageAsync(card, $"O Claude do tratamento não pôde ser chamado: {e.Message}", cancellationToken);
        }
    }

    private static bool CanDecide(CaseActor actor, ExecutionPlan? plan) =>
        !actor.IsSpecialist && (actor.IsManager || (actor.UserId is { } me && plan?.CreatedByUserId == me));

    private async Task<List<string>> KnowsAsync(string card, CancellationToken cancellationToken)
    {
        var knows = new List<string> { "Card + discussion" };
        var summaries = await planApp.GetByCardAsync(card, cancellationToken);
        if (summaries.Where(s => s.Phase == ExecutionPhase.Analysis).MaxBy(s => s.VersionNumber) is { } a)
            knows.Add($"Análise v{a.VersionNumber}");
        if (summaries.Where(s => s.Phase == ExecutionPhase.Correction).MaxBy(s => s.VersionNumber) is { } c)
            knows.Add($"Correção v{c.VersionNumber} · {StatusLabel(c.Status)}");
        knows.AddRange(["Base Solvace", "Banco (só leitura)", "Código"]);
        return knows;
    }

    private static string StatusLabel(string status) => status switch
    {
        ExecutionStatus.Completed => "concluída",
        ExecutionStatus.Cancelled => "cancelada",
        ExecutionStatus.Failed => "falhou",
        ExecutionStatus.Paused => "pausada",
        _ => "em andamento"
    };

    private static void RequireSpecialist(CaseActor actor)
    {
        if (!actor.IsSpecialist)
            throw new ExecutionForbiddenException("Só o Especialista do caso (pelo executor) usa esta operação.");
    }

    private static void ValidateUploads(IReadOnlyList<ExecutionArtifactUpload> uploads)
    {
        if (uploads.Count > CaseMessage.MaxAttachments)
            throw new DomainException($"Anexe no máximo {CaseMessage.MaxAttachments} arquivos por mensagem.");
        foreach (var u in uploads)
        {
            if (u.Data.LongLength == 0)
                throw new DomainException($"O arquivo '{u.FileName}' está vazio.");
            if (u.Data.LongLength > CaseFile.MaxFileBytes)
                throw new DomainException($"O arquivo '{u.FileName}' passa do limite de {CaseFile.MaxFileBytes / (1024 * 1024)} MB.");
        }
    }

    private async Task NotifyAsync(string card, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await realTime.NotifyGroupAsync(ExecutionPlanRealTimeEvents.Group(card), ExecutionPlanRealTimeEvents.EventCaseUpdated, payload, cancellationToken);
        }
        catch
        {
            // best-effort: a tela também recarrega ao abrir
        }
    }

    private static string Plain(string text) => text.Replace('\n', ' ').Replace("**", string.Empty).Trim();

    private static string Cut(string? text, int max)
    {
        var t = text ?? string.Empty;
        return t.Length <= max ? t : t[..max] + "…";
    }
}
