using System.Text;
using System.Text.Json;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>Sem executor que possa responder a pergunta agora (a tela oferece a IA do servidor desta vez).</summary>
public class BaseAskUnavailableException(string code, string message) : Exception(message)
{
    public const string NoExecutor = "NO_EXECUTOR";
    public string Code { get; } = code;
}

/// <summary>
/// 0102: perguntas da Base Solvace pelo executor — o Claude Code da máquina de quem perguntou consulta a Base pelas
/// ferramentas <c>prmake_base_*</c> e devolve a resposta com <c>prmake_base_answer</c>. Pista própria (até
/// <see cref="BaseAskSlots"/> por máquina, fora do limite das análises), só executor ≥ <see cref="BaseAskAgentVersion"/>,
/// sempre na máquina de quem perguntou (card 78723).
/// </summary>
public partial class ExecutionQueueApplication
{
    /// <summary>"Fase" gravada no pedido (o modelo e o esforço vêm de BaseAskExecutorModel/Effort/DeepEffort).</summary>
    public const string BaseAskPhase = "base-ask";
    public const string BaseAskDeepPhase = "base-ask-deep";
    public const int BaseAskSlots = 2;
    /// <summary>Executor que conhece a pista: token restrito, <c>--effort</c>, pasta neutra e anexos do claim.</summary>
    public const string BaseAskAgentVersion = "1.9.0";

    public static bool SupportsBaseAsk(ExecutionWorker worker) => !IsOlder(worker.AgentVersion, BaseAskAgentVersion);

    public async Task<BaseQuestionResponse> AskBaseAsync(ExecutionActor requester, string question, bool deep, IReadOnlyList<BaseQuestionFile> files,
        CancellationToken cancellationToken)
    {
        if (requester.UserId is not { } owner || owner == Guid.Empty)
            throw new ExecutionForbiddenException("Usuário não identificado.");
        var now = DateTimeOffset.UtcNow;
        var workers = (await _queue.GetWorkersByOwnerAsync(owner, cancellationToken)).Where(w => !w.IsRevoked).ToList();
        if (workers.Count == 0)
            throw new BaseAskUnavailableException(BaseAskUnavailableException.NoExecutor,
                "Você ainda não tem o executor do PRMake numa máquina — instale em \"Meus executores\" ou responda pela IA do servidor.");
        var ready = workers.Where(w => w.IsOnline(now) && w.Status == ExecutionWorkerStatus.Active).ToList();
        if (ready.Count == 0)
            throw new BaseAskUnavailableException(BaseAskUnavailableException.NoExecutor, workers.Count == 1
                ? $"O executor de {workers[0].Name} está {(workers[0].IsOnline(now) ? "pausado" : "offline")} — ligue a máquina/retome o executor ou responda pela IA do servidor."
                : "Nenhum dos seus executores está online e ativo — ligue uma máquina ou responda pela IA do servidor.");
        _queue.ClearTracking();

        var q = new BaseQuestion(owner, requester.Name, question, deep, files, now);
        var request = new ExecutionRequest(q.Key, ExecutionRequestKind.BaseAsk, ExecutionRequestSource.Base, owner, requester.Name,
            owner, requester.Name, null, null, null, null, false, now);
        if (deep) request.MarkDeep(now);
        q.AttachRequest(request.Id);
        _queue.AddBaseQuestion(q);
        var saved = await AddRequestAsync(request, cancellationToken) ?? request;
        await UpdateWaitReasonsAsync(owner, now, cancellationToken);
        return await BaseQuestionResponseAsync(q, saved, cancellationToken);
    }

    public async Task<BaseQuestionResponse> GetBaseQuestionAsync(Guid questionId, Guid? userId, CancellationToken cancellationToken)
    {
        var q = await RequireBaseQuestionAsync(questionId, userId, cancellationToken);
        var r = await RequireRequestAsync(q.RequestId, cancellationToken);
        return await BaseQuestionResponseAsync(q, r, cancellationToken);
    }

    public async Task<BaseQuestionResponse> CancelBaseQuestionAsync(Guid questionId, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var q = await RequireBaseQuestionAsync(questionId, actor.UserId, cancellationToken);
        var r = await RequireRequestAsync(q.RequestId, cancellationToken);
        if (r.IsActive && !q.IsAnswered)
        {
            var now = DateTimeOffset.UtcNow;
            r = await MutateRequestAsync(r.Id, x => x.Cancel(actor.Name, "Pergunta cancelada pela tela", now), cancellationToken);
            await NotifyAsync(r, cancellationToken);
        }
        return await BaseQuestionResponseAsync(q, r, cancellationToken);
    }

    /// <summary>
    /// A resposta do Claude (ferramenta <c>prmake_base_answer</c>, com o token do pedido): só a pergunta DESTE pedido e
    /// enquanto ele está ativo. <paramref name="answerJson"/> é o <c>ArchitectureAskResponse</c> já montado (camelCase).
    /// </summary>
    public async Task<BaseQuestion> SaveBaseAnswerAsync(Guid requestId, Guid questionId, string answerJson, CancellationToken cancellationToken)
    {
        var q = await _queue.GetBaseQuestionAsync(questionId, cancellationToken);
        if (q is null || q.RequestId != requestId)
            throw new ExecutionForbiddenException("Esta pergunta não é a deste pedido — responda só a pergunta do prompt.");
        var r = await RequireRequestAsync(requestId, cancellationToken);
        if (!r.IsActive)
            throw new DomainException("O pedido desta pergunta já terminou (cancelado ou vencido) — a resposta não foi gravada.");
        q.SetAnswer(answerJson, DateTimeOffset.UtcNow);
        await _queue.SaveChangesAsync(cancellationToken);
        await NotifyBaseAsync(r, ExecutionPlanRealTimeEvents.Actions.Answer, cancellationToken);
        return q;
    }

    /// <summary>A pergunta do pedido (para o MCP conferir o dono e o tipo).</summary>
    public Task<BaseQuestion?> GetBaseQuestionForRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        _queue.GetBaseQuestionByRequestAsync(requestId, cancellationToken);

    /// <summary>O pedido da pergunta está ativo? (validade do token restrito).</summary>
    public async Task<bool> IsBaseAskActiveAsync(Guid requestId, CancellationToken cancellationToken) =>
        await _queue.GetRequestAsync(requestId, cancellationToken) is { Kind: ExecutionRequestKind.BaseAsk, IsActive: true };

    private async Task<BaseQuestion> RequireBaseQuestionAsync(Guid questionId, Guid? userId, CancellationToken cancellationToken)
    {
        var q = await _queue.GetBaseQuestionAsync(questionId, cancellationToken) ?? throw new ExecutionPlanNotFoundException("Pergunta não encontrada.");
        if (userId is null || q.OwnerUserId != userId)
            throw new ExecutionPlanNotFoundException("Pergunta não encontrada.");
        return q;
    }

    private async Task<BaseQuestionResponse> BaseQuestionResponseAsync(BaseQuestion q, ExecutionRequest r, CancellationToken cancellationToken)
    {
        var request = await RespondAsync(r, cancellationToken);
        var status = q.IsAnswered ? ExecutionRequestStatus.Done : r.Status == ExecutionRequestStatus.Done ? ExecutionRequestStatus.Failed : r.Status;
        string? error = status switch
        {
            ExecutionRequestStatus.Failed when r.Status == ExecutionRequestStatus.Done =>
                "O Claude terminou sem gravar a resposta. Tente de novo ou responda pela IA do servidor.",
            ExecutionRequestStatus.Failed => r.LastError ?? "O executor não conseguiu responder.",
            ExecutionRequestStatus.Expired => "Nenhuma máquina pegou a pergunta a tempo.",
            _ => null
        };
        JsonElement? answer = null;
        if (q.Answer is { } json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                answer = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                error = "A resposta gravada está corrompida.";
            }
        }
        return new BaseQuestionResponse
        {
            Id = q.Id,
            Key = q.Key,
            Question = q.Question,
            Deep = q.Deep,
            HasFiles = q.Files is not null,
            Status = status,
            RequestId = r.Id,
            WaitReason = status == ExecutionRequestStatus.Queued ? request.WaitReason : null,
            WaitCode = status == ExecutionRequestStatus.Queued ? request.WaitCode : null,
            WorkerName = r.WorkerName,
            Activity = ExecutionRequestStatus.IsActive(status) ? request.CurrentActivity : null,
            Answer = answer,
            Error = error,
            Model = r.Model,
            CostUsd = r.CostUsd,
            CreatedAt = q.CreatedAt,
            StartedAt = r.StartedAt,
            AnsweredAt = q.AnsweredAt
        };
    }

    /// <summary>Prompt e anexos do pedido (no claim).</summary>
    private async Task<(string Prompt, List<BaseQuestionFile>? Files)> BaseAskPromptAsync(ExecutionRequest r, CancellationToken cancellationToken)
    {
        var q = await _queue.GetBaseQuestionByRequestAsync(r.Id, cancellationToken)
                ?? throw new ExecutionPlanNotFoundException("A pergunta deste pedido não existe mais.");
        var files = q.GetFiles().ToList();
        return (BaseAskPrompt(q, files), files.Count > 0 ? files : null);
    }

    /// <summary>
    /// O Claude consulta a Base (itens da engenharia reversa, seções, Knowledge Center) pelas ferramentas MCP e responde no
    /// formato do Pergunte (0071: para quem USA o sistema). Rápido: poucas buscas e leituras; a fundo, pode confirmar no código.
    /// </summary>
    public static string BaseAskPrompt(BaseQuestion q, IReadOnlyList<BaseQuestionFile> files)
    {
        var sb = new StringBuilder()
            .AppendLine($"Voce responde uma pergunta de {q.OwnerName} sobre os sistemas da Solvace consultando a Base Solvace (engenharia reversa " +
                        "por item — regras RN, casos de uso UC, telas TELA, perfis PRF, notificacoes NTF, integracoes INT —, secoes e artigos do " +
                        "Knowledge Center) pelas ferramentas MCP do PRMake. A pessoa esta esperando na tela.")
            .AppendLine()
            .AppendLine($"ID da pergunta: {q.Id}")
            .AppendLine("PERGUNTA:")
            .AppendLine("<<<")
            .AppendLine(q.Question)
            .AppendLine(">>>");
        if (files.Count > 0)
        {
            sb.AppendLine()
                .AppendLine("A pessoa anexou arquivos (parte da pergunta — ex.: print de um erro). Leia cada um com Read ANTES de buscar; os " +
                            "arquivos estao na pasta da variavel de ambiente PRMAKE_FILES_DIR (`echo $PRMAKE_FILES_DIR` no Bash se precisar):");
            foreach (var f in files) sb.AppendLine($"- {f.Name} ({f.MediaType})");
            sb.AppendLine("Use o texto de erro, o nome da tela/menu/botao e o modulo que aparecem neles como termos de busca.");
        }
        sb.AppendLine()
            .AppendLine("Como fazer:")
            .AppendLine("1. prmake_base_search com os termos como a pessoa chama a coisa na tela e sinonimos (planta = unidade = site; pt e en). " +
                        "Ate 3 buscas, mudando os termos se a primeira nao achar.")
            .AppendLine("2. prmake_base_get nos itens que respondem (varios refs numa chamada so). Siga uma referencia citada no texto so se ela " +
                        "for necessaria para responder (ex.: o perfil que pode, a tela do passo).")
            .AppendLine(q.Deep
                ? "3. ANALISAR A FUNDO: a pessoa quer uma resposta mais completa. Leia mais itens e, se a Base nao confirmar algo importante, " +
                  "confirme no codigo dos repositorios desta maquina (somente leitura: Grep/Read no 'Onde:' que o item cita). Diga o que veio do codigo."
                : "3. Seja rapido: no maximo umas 6 chamadas no total. Nao leia codigo.")
            .AppendLine("4. Responda com prmake_base_answer (UMA vez) e encerre:")
            .AppendLine($"   question=\"{q.Id}\", answer=markdown, coverage=answered|partial|not-found, kind=operacao|regra|tecnica|outra, " +
                        "refs=\"modulo#ID, ...\" (os itens que voce usou, do mais util para o menos util, ate 6; ART-n para artigos).")
            .AppendLine()
            .AppendLine("A resposta (answer) e para quem USA o sistema (QA, suporte, gestor, consultor) — nao programa:")
            .AppendLine("- Pergunta de COMO FAZER: titulos em negrito **Resumo** (1-2 frases), **Quem pode**, **Antes de comecar**, **Passo a passo** " +
                        "(lista numerada na ordem da tela, com os nomes de menus/botoes/campos como aparecem), **O que acontece depois**, **Cuidados**, " +
                        "**Se nao funcionar**, **Nao confirmado** — omita o titulo cujo conteudo a Base nao traz.")
            .AppendLine("- Pergunta TECNICA: direto, em topicos curtos; pode citar tabelas, endpoints, classes e arquivos.")
            .AppendLine("- Outras: direto, frases ou lista curta (ate ~8 itens).")
            .AppendLine("- Linguagem de negocio; nada de endpoint, tabela, classe ou codigo de item (RN-..., UC-...) no texto, exceto na pergunta tecnica. " +
                        "No maximo ~250 palavras.")
            .AppendLine("- Use SO o que a Base (ou o codigo, a fundo) diz; nao invente. Se a Base nao responde, diga em uma frase e use coverage not-found.")
            .AppendLine("- Escreva em portugues do Brasil com acentos.")
            .AppendLine()
            .AppendLine("Nao grave nada alem do prmake_base_answer. (Executor do PRMake, sem terminal: responda e encerre.)");
        return sb.ToString();
    }

    private async Task NotifyBaseAsync(ExecutionRequest request, string action, CancellationToken cancellationToken)
    {
        try
        {
            var response = request.ToResponse();
            await _realTime.NotifyGroupAsync(
                ExecutionPlanRealTimeEvents.Group(request.CardNumber),
                ExecutionPlanRealTimeEvents.EventPlanUpdated,
                new
                {
                    cardNumber = request.CardNumber, planId = (Guid?)null, action, requestId = request.Id, status = request.Status,
                    activity = response.CurrentActivity, recent = response.RecentActivities
                },
                cancellationToken);
        }
        catch
        {
            // best-effort: a tela também consulta enquanto a pergunta está ativa
        }
    }

    /// <summary>O pedido terminou: os anexos não servem mais.</summary>
    private async Task AfterBaseAskAsync(ExecutionRequest r, CancellationToken cancellationToken)
    {
        try
        {
            if (await _queue.GetBaseQuestionByRequestAsync(r.Id, cancellationToken) is { Files: not null } q)
            {
                q.DropFiles();
                await _queue.SaveChangesAsync(cancellationToken);
            }
        }
        catch (ExecutionPlanConcurrencyException)
        {
            _queue.ClearTracking();
        }
    }
}
