using System.ComponentModel;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using solvace.azure.application.Contract;
using solvace.azure.domain.Models;
using solvace.azure.domain.Requests;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.prform.Execution;

/// <summary>
/// Servidor MCP remoto do PRMake (0039): as operações do plano de execução que a skill fazia pelo
/// <c>prmake-plan.sh</c>, como ferramentas — sobre a mesma camada de aplicação dos controllers. Quem chama é sempre
/// tratado como executor (mesma semântica do header <c>X-Execution-Client: skill</c>). Respostas enxutas: só o que a
/// skill usa.
/// </summary>
[McpServerToolType]
public partial class PrmakeMcpTools(
    IExecutionPlanApplication plans,
    IExecutionQueueApplication queue,
    IExecutionTimelineWriter timeline,
    DevOpsActionRunner devops,
    IAzureService azure,
    solvace.prform.Skills.SkillsConfigService skillsConfig,
    IHttpContextAccessor http,
    solvace.timeline.application.Contracts.IUserRepository users,
    solvace.knowledge.application.Contracts.IReverseEngineeringApplication reverse)
{
    public const string Instructions =
        "PRMake: plano de execucao do card (etapas, andamento, perguntas ao usuario, comentarios e anexos, Timeline, " +
        "mover o card no DevOps). Toda gravacao no PRMake passa por aqui ou pelo prmake-plan.sh. Informe sempre o card; " +
        "'phase' (analysis|correction) escolhe o plano quando o card tem os dois (padrao: o plano aberto mais recente). " +
        "Ciclo do plano: prmake_advance (conclui uma etapa e inicia a proxima), prmake_block (trava esperando o usuario), " +
        "prmake_ask/prmake_answer (nova rodada da analise: a pergunta revisada leva replaces e as que perderam o sentido vao para " +
        "prmake_cancel_questions — so ficam abertas as que ainda decidem o plano), prmake_correction (plano de correcao), prmake_file (grava script/analise/chamado nos arquivos do plano). " +
        "Git e o 'status' final ficam no prmake-plan.sh. " +
        "Base Solvace ANTES do codigo (0052): prmake_base_search (itens da engenharia reversa: regras RN, casos de uso UC, telas, " +
        "endpoints, tabelas, integracoes) -> prmake_base_get (so o texto do item) -> prmake_base_impact (quem mais usa); informe o card. " +
        "Codigo so para confirmar o 'Onde:' que o item cita.";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [McpServerTool(Name = "prmake_plan", ReadOnly = true)]
    [Description("Estado do plano do card: status, etapas (chave, status, executor, aguardando quem, motivo), perguntas abertas, pendencias do usuario e o pedido da fila.")]
    public Task<string> Plan(
        [Description("Numero do card")] string card,
        [Description("analysis | correction (opcional)")] string? phase = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var plan = await plans.GetAsync(await PlanIdAsync(card, phase, ct), ct);
        var request = (await queue.GetCardAsync(card, null, ct)).Active;
        return Serialize(new
        {
            plan.Id, plan.Phase, plan.Status, plan.StatusReason, plan.Title,
            steps = plan.Steps.OrderBy(s => s.Order).Select(s => new
            {
                s.Key, s.Title, s.Status, s.Executor, s.Kind, s.WaitingOn, reason = s.StatusReason, s.DependsOn, s.Repository,
                // 0041: o que o resume-info mostrava — onde parou e o que estava fazendo.
                s.Checkpoint, s.Activity,
                links = plan.Links.Where(l => l.StepKey == s.Key).Select(l => new { l.Kind, title = l.Title ?? l.Url, l.Status, l.Url }).ToList() is { Count: > 0 } ls ? ls : null,
                // 0050: arquivos anexados à etapa (pela key) — cite-os pelo nome na etapa, na mensagem final e na Timeline
                files = plan.Artifacts.Where(a => a.StepKey == s.Key && a.NoteId is null).Select(a => new { a.Name, a.Kind }).ToList() is { Count: > 0 } fs ? fs : null
            }),
            openQuestions = plan.Questions.Count(q => q.Status == ExecutionQuestionStatus.Open),
            userActions = plan.UserActions.Count,
            lastUserNote = plan.Notes.Where(n => !n.FromExecutor).Select(n => n.Number).DefaultIfEmpty(0).Max(),
            plan.NotesReadNumber,
            request = request is null ? null : new { request.Id, request.Kind, request.Status, request.Attempts, request.WaitReason }
        });
    });

    [McpServerTool(Name = "prmake_steps")]
    [Description("Cria ou atualiza as etapas do plano (upsert pela chave). executor: claude|user; kind: task|code|pr|ticket|question|validation.")]
    public Task<string> Steps(
        string card,
        [Description("Etapas: [{ key, title, description?, executor?, kind?, repository?, dependsOn? }]")] List<ExecutionStepDefinition> steps,
        string? phase = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var plan = await plans.UpsertStepsAsync(await PlanIdAsync(card, phase, ct), new UpsertExecutionStepsRequest { Steps = steps }, await ActorAsync(ct), ct);
        return Serialize(new { plan.Status, steps = plan.Steps.OrderBy(s => s.Order).Select(s => new { s.Key, s.Status }) });
    });

    [McpServerTool(Name = "prmake_step")]
    [Description("Atualiza uma etapa: status (pending|running|waiting|completed|failed|cancelled), motivo, atividade (o que esta fazendo agora), checkpoint (onde retomar), waitingOn (answer|user|external).")]
    public Task<string> Step(
        string card,
        [Description("Chave da etapa")] string key,
        string? status = null,
        string? reason = null,
        string? activity = null,
        string? checkpoint = null,
        string? waitingOn = null,
        string? phase = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var step = await plans.UpdateStepAsync(await PlanIdAsync(card, phase, ct), key, new UpdateExecutionStepRequest
        {
            Status = status, Reason = reason, Activity = activity, Checkpoint = checkpoint, WaitingOn = waitingOn
        }, await ActorAsync(ct), ct);
        return Serialize(new { step.Key, step.Status, step.WaitingOn, reason = step.StatusReason });
    });

    [McpServerTool(Name = "prmake_log")]
    [Description("Registra andamento no plano (aparece ao vivo na tela). kind: info|progress|finding|decision|warning|error.")]
    public Task<string> Log(string card, string message, string? kind = null, string? stepKey = null, string? phase = null, CancellationToken ct = default) => Safe(async () =>
    {
        var count = await plans.AppendLogsAsync(await PlanIdAsync(card, phase, ct), new AppendExecutionLogsRequest
        {
            Logs = [new ExecutionLogItem { Message = message, Kind = kind, StepKey = stepKey }]
        }, ct);
        return count > 0 ? "ok" : "ignorado (duplicado)";
    });

    [McpServerTool(Name = "prmake_control")]
    [Description("Checagem entre etapas: action continue|wait (pausado pela tela)|stop (cancelado/concluido), etapas prontas, aguardando, perguntas abertas e se os comentarios do usuario mudaram.")]
    public Task<string> Control(string card, string? phase = null, CancellationToken ct = default) => Safe(async () =>
    {
        var c = await plans.ControlAsync(await PlanIdAsync(card, phase, ct), ct);
        return Serialize(new
        {
            c.Action, c.Status, c.StatusReason, c.StatusChangedBy, c.ReadySteps, c.WaitingSteps, c.CancelledSteps,
            c.OpenQuestions, c.UserPending, c.LastUserNoteNumber,
            // 0050: etapa de chamado com o usuário sem script/texto do chamado — anexe com prmake_file antes de seguir
            ticketStepsMissingFiles = c.TicketStepsMissingFiles.Count > 0 ? c.TicketStepsMissingFiles : null
        });
    });

    [McpServerTool(Name = "prmake_plan_status")]
    [Description("Muda o status do plano: running (retomar), completed (com resumo), failed (com motivo).")]
    public Task<string> PlanStatus(string card, string status, string? reason = null, string? summary = null, string? phase = null, CancellationToken ct = default) => Safe(async () =>
    {
        var plan = await plans.ChangeStatusAsync(await PlanIdAsync(card, phase, ct), new ChangeExecutionPlanStatusRequest
        {
            Status = status, Reason = reason, Summary = summary
        }, await ActorAsync(ct), ct);
        return Serialize(new { plan.Status, plan.StatusReason });
    });

    [McpServerTool(Name = "prmake_ask")]
    [Description("Pergunta ao usuario pela tela do card (a etapa fica aguardando a resposta). Cada opcao: { label (a propria opcao), description?, recommended? }. " +
                 "Nova rodada (comentario/anexo novo): a pergunta revisada leva replaces: [numeros das antigas] — as abertas sao canceladas; " +
                 "nunca escreva 'substitui a pergunta N' no texto sem o replaces. A resposta lista as perguntas anteriores ainda abertas.")]
    public Task<string> Ask(
        string card,
        [Description("Perguntas: [{ stepKey?, text, options?: [{ label, description?, recommended? }], allowFreeText?, replaces?: [numeros] }]")] List<ExecutionQuestionItem> questions,
        string? phase = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var planId = await PlanIdAsync(card, phase, ct);
        var created = await plans.AskAsync(planId, new AskExecutionQuestionsRequest { Questions = questions }, await ActorAsync(ct), ct);
        var newIds = created.Select(q => q.Id).ToHashSet();
        var plan = await plans.GetAsync(planId, ct);
        var stillOpen = plan.Questions.Where(q => q.Status == ExecutionQuestionStatus.Open && !newIds.Contains(q.Id)).OrderBy(q => q.Order)
            .Select(q => new { q.Order, q.Text }).ToList();
        return Serialize(new
        {
            created = created.Select(q => new { q.Id, q.Order, q.Status }),
            replaced = plan.Questions.Where(q => q.ReplacedBy is not null && created.Any(c => c.Order == q.ReplacedBy)).OrderBy(q => q.Order)
                .Select(q => new { q.Order, q.Status, q.ReplacedBy }),
            stillOpen,
            hint = stillOpen.Count == 0 ? null
                : "Perguntas anteriores ainda abertas: as que perderam o sentido com esta rodada -> prmake_cancel_questions(card, numbers, reason)."
        });
    });

    [McpServerTool(Name = "prmake_cancel_questions")]
    [Description("Cancela perguntas abertas que perderam o sentido (nova rodada da analise, o usuario ja respondeu em comentario, a duvida sumiu). " +
                 "Informe os numeros e o motivo (aparece na tela e na Timeline). Pergunta que so mudou de texto: crie a nova com replaces no prmake_ask.")]
    public Task<string> CancelQuestions(string card, [Description("Numeros das perguntas (1, 2...)")] List<int> numbers,
        [Description("Motivo curto, ex.: 'Ja nao se aplica: o print mostrou que o erro e de permissao'")] string reason,
        string? phase = null, CancellationToken ct = default) => Safe(async () =>
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new McpException("Informe o motivo do cancelamento.");
        var result = await plans.CancelQuestionsAsync(await PlanIdAsync(card, phase, ct),
            new CancelExecutionQuestionsRequest { Numbers = numbers, Reason = reason }, await ActorAsync(ct), ct);
        return Serialize(result.Select(q => new { q.Order, q.Status, q.CancelReason }));
    });

    [McpServerTool(Name = "prmake_answers", ReadOnly = true)]
    [Description("Perguntas do plano com as respostas (de quem, por onde). replacedBy = a resposta foi superada pela da pergunta indicada.")]
    public Task<string> Answers(string card, string? phase = null, CancellationToken ct = default) => Safe(async () =>
    {
        var plan = await plans.GetAsync(await PlanIdAsync(card, phase, ct), ct);
        return Serialize(plan.Questions.OrderBy(q => q.Order).Select(q => new
        {
            q.Order, q.StepKey, q.Text, q.Status, q.Answer, q.AnsweredBy, q.AnsweredVia, q.CancelReason,
            // Respondida e depois substituida: a resposta nao vale mais — vale a da pergunta nova.
            q.ReplacedBy
        }));
    });

    [McpServerTool(Name = "prmake_notes")]
    [Description("Comentarios do usuario (e do Claude) no plano do card, com os anexos numerados (#n). Ler marca como lidos ate o ultimo.")]
    public Task<string> Notes(string card, [Description("So o comentario de numero n (opcional)")] int? number = null, CancellationToken ct = default) => Safe(async () =>
    {
        var notes = await plans.GetNotesByCardAsync(card.Trim(), ct, fromExecutor: true);
        return Serialize(notes.Where(n => number is null || n.Number == number).Select(n => new
        {
            n.Number, n.PlanPhase, n.StepKey, n.AuthorName, n.FromExecutor, n.CreatedAt, n.Text,
            attachments = n.Attachments.Select(a => new { a.Number, a.Name, a.ContentType, a.Size })
        }));
    });

    [McpServerTool(Name = "prmake_attachment", ReadOnly = true)]
    [Description("Abre um anexo do card: imagem volta como imagem; texto volta como texto. Referencia: '#12', '12', 'imagem 2' (a 2a imagem), ou o nome do arquivo.")]
    public Task<CallToolResult> Attachment(string card, [Description("'#12', 'imagem 2', 'print.png'...")] string reference, CancellationToken ct = default) => Safe(async () =>
    {
        var all = (await plans.GetNotesByCardAsync(card.Trim(), ct)).SelectMany(n => n.Attachments).OrderBy(a => a.Number).ToList();
        var found = Resolve(all, reference);
        if (found is null)
            return Error($"Anexo '{reference}' nao encontrado. Anexos do card: " +
                         (all.Count == 0 ? "nenhum" : string.Join(", ", all.Select(a => $"#{a.Number} {a.Name}"))));
        var file = await plans.GetArtifactFileAsync(found.PlanId, found.Id, ct);
        var header = new TextContentBlock { Text = $"#{found.Number} {found.Name} ({found.ContentType}, {found.Size} bytes)" };
        if (found.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && !found.ContentType.Contains("svg"))
            return new CallToolResult { Content = [header, ImageContentBlock.FromBytes(file.Data, found.ContentType)] };
        if (LooksLikeText(found.ContentType, file.Data))
        {
            var text = Encoding.UTF8.GetString(file.Data);
            if (text.Length > 60_000) text = text[..60_000] + "\n... (cortado)";
            return new CallToolResult { Content = [header, new TextContentBlock { Text = text }] };
        }
        return new CallToolResult
        {
            Content = [header, new TextContentBlock { Text = "Arquivo binario: baixe com prmake-plan.sh attachment para abrir localmente." }]
        };
    });

    [McpServerTool(Name = "prmake_timeline")]
    [Description("Escreve na Timeline do card (markdown). O PRMake ja escreve sozinho os marcos do plano — nao duplique.")]
    public Task<string> Timeline(string card, string markdown, CancellationToken ct = default) => Safe(async () =>
    {
        var actor = await ActorAsync(ct);
        await timeline.WriteAsync(card.Trim(), markdown, actor.UserId, actor.Name, ct);
        return "ok";
    });

    [McpServerTool(Name = "prmake_devops", Destructive = true)]
    [Description("Move/atualiza o card no Azure DevOps pelas regras configuradas no PRMake e registra na Timeline. action: dev-test-in-qa | ready-for-qa | test-in-production | zero-remaining | initial-estimate | classify. ready-for-qa so com a etapa validar-qa concluida pelo usuario ou com 'authorization' (quem autorizou e quando, explicitamente). classify: 'preset' (chave de prmake_devops_config) ou os tres valores.")]
    public Task<string> DevOps(string card, string action,
        [Description("Autorizacao explicita do usuario para ready-for-qa")] string? authorization = null,
        [Description("classify: chave da classificacao pronta")] string? preset = null,
        [Description("classify: Resolution Type")] string? resolutionType = null,
        [Description("classify: General Classification")] string? generalClassification = null,
        [Description("classify: Classification")] string? classification = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var id = card.Trim();
        Func<IDevOpsActionsService, Task<DevOpsActionResponse>> run = action.Trim().ToLowerInvariant() switch
        {
            "dev-test-in-qa" => a => a.MoveToDevTestInQaAsync(id, ct),
            "test-in-production" => a => a.MoveToTestInProductionAsync(id, ct),
            "zero-remaining" => a => a.ZeroRemainingAsync(id, ct),
            "initial-estimate" => a => a.SetInitialEstimateAsync(id, ct),
            "ready-for-qa" => a => a.MoveToReadyForQaAsync(id, ct),
            "classify" => a => a.ClassifyAsync(id, new ClassifyCardRequest
            {
                Preset = preset, ResolutionType = resolutionType, GeneralClassification = generalClassification, Classification = classification
            }, ct),
            _ => throw new McpException($"Acao desconhecida: {action}")
        };
        if (action.Trim().Equals("ready-for-qa", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(authorization) && !await QaValidatedAsync(id, ct))
            throw new McpException("Ready for QA so com a etapa validar-qa concluida pelo usuario no plano ou com a autorizacao explicita dele (informe em 'authorization').");
        return Serialize(await devops.RunAsync(id, run, http.HttpContext?.User ?? new ClaimsPrincipal(), ct));
    });

    [McpServerTool(Name = "prmake_devops_config", ReadOnly = true)]
    [Description("Regras das acoes do DevOps configuradas no PRMake (estados/colunas de cada acao, area exigida, comentarios) e as classificacoes prontas (presets).")]
    public Task<string> DevOpsConfig(CancellationToken ct = default) => Safe(async () =>
        Serialize(new { actions = await devops.ConfigAsync(ct), classifications = await devops.ClassificationsAsync(ct) }));

    [McpServerTool(Name = "prmake_config", ReadOnly = true)]
    [Description("Configuracao das skills no PRMake (Skills Configurations: fluxo de branches, padroes de nome/commit/titulo, repositorio padrao...), quais prompts existem e os nomes dos campos do DevOps. Nunca escreva essas regras de memoria.")]
    public Task<string> Config(CancellationToken ct = default) => Safe(async () =>
    {
        var c = await skillsConfig.BuildAsync(ct);
        return Serialize(new
        {
            c.Available, c.Settings,
            prompts = new { bug = c.Prompts.Bug is not null, userStory = c.Prompts.UserStory is not null, summary = c.Prompts.Summary is not null },
            c.Fields
        });
    });

    [McpServerTool(Name = "prmake_card", ReadOnly = true)]
    [Description("Card do Azure DevOps compacto: titulo, tipo, estado, area, responsavel, prioridade, tags, descricao e repro steps (texto), ultimos comentarios e alertas de fechamento.")]
    public Task<string> Card(string card, [Description("Quantos comentarios (do mais recente), padrao 10")] int comments = 10,
        CancellationToken ct = default) => Safe(async () =>
    {
        AzureCardFullResponse? full;
        try
        {
            full = await azure.GetCardFullAsync(card.Trim(), ct);
        }
        catch (Exception e) when (e is not (OperationCanceledException or McpException or InvalidOperationException
                                            or solvace.prform.application.UserIntegrations.PersonalIntegrationRequiredException))
        {
            // O DevOps respondeu erro (card inexistente, PAT sem acesso): a mensagem dele vai para o Claude.
            throw new McpException($"Azure DevOps: {e.Message}", e);
        }
        if (full is null) throw new McpException($"Card {card} nao encontrado no Azure DevOps.");
        if (!string.IsNullOrEmpty(full.Error)) throw new McpException(full.Error);
        string? F(string key, int max = 4000)
        {
            if (!full.Fields.TryGetValue(key, out var v)) return null;
            var text = v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Object when v.TryGetProperty("displayName", out var dn) => dn.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => v.ToString()
            };
            text = StripHtml(text);
            return string.IsNullOrWhiteSpace(text) ? null : text.Length <= max ? text : text[..max] + " (...)";
        }
        return Serialize(new
        {
            id = full.Id, full.Url,
            title = F("System.Title"), type = F("System.WorkItemType"), state = F("System.State"), area = F("System.AreaPath"),
            assignedTo = F("System.AssignedTo"), priority = F("Microsoft.VSTS.Common.Priority"), severity = F("Microsoft.VSTS.Common.Severity"),
            tags = F("System.Tags"), created = F("System.CreatedDate"), changed = F("System.ChangedDate"),
            description = F("System.Description"), reproSteps = F("Microsoft.VSTS.TCM.ReproSteps"),
            comments = full.Comments.OrderByDescending(x => x.CreatedDate).Take(Math.Clamp(comments, 0, 50))
                .Select(x => new { by = x.CreatedByName, at = x.CreatedDate, text = Trim(StripHtml(x.Text), 1500) }),
            alerts = full.Alerts
        });
    });

    [McpServerTool(Name = "prmake_queue", ReadOnly = true)]
    [Description("Pedido do executor no card (na fila, rodando, falhou), o que o Claude esta fazendo agora (activity) e os ultimos pedidos.")]
    public Task<string> Queue(string card, CancellationToken ct = default) => Safe(async () =>
    {
        var state = await queue.GetCardAsync(card.Trim(), null, ct);
        return Serialize(new
        {
            active = state.Active is { } a
                ? new
                {
                    a.Id, a.Kind, a.Status, a.WorkerName, a.Attempts, a.MaxAttempts, a.WaitCode, a.WaitReason, a.LastError, a.CreatedAt, a.StartedAt,
                    // 0050: o que o Claude está fazendo agora (rótulo do executor) e as últimas atividades
                    activity = a.CurrentActivity,
                    recentActivities = a.RecentActivities
                }
                : null,
            recent = state.Recent.Select(r => new { r.Id, r.Kind, r.Status, r.FinishedReason, r.LastError, r.CostUsd })
        });
    });

    // ── 0041: o ciclo inteiro do plano ──────────────────────────────────────────────────────────

    [McpServerTool(Name = "prmake_advance")]
    [Description("Conclui uma etapa e inicia a proxima numa chamada (equivale a prmake-plan.sh advance). 'to' vazio = so conclui. 'message' vira registro da etapa concluida (kind: progress|finding|decision|warning).")]
    public Task<string> Advance(string card, [Description("Etapa que terminou")] string from, [Description("Proxima etapa (opcional)")] string? to = null,
        string? message = null, string? kind = null, string? phase = null, CancellationToken ct = default) => Safe(async () =>
    {
        var planId = await PlanIdAsync(card, phase, ct);
        var actor = await ActorAsync(ct);
        // 0052: módulo com engenharia reversa completa — a investigação só conclui consultando a base ou citando item/lacuna.
        if (await reverse.CheckGateAsync(card, from, message, ct) is { } gate)
            throw new McpException(gate);
        if (!string.IsNullOrWhiteSpace(message))
            await plans.AppendLogsAsync(planId, new AppendExecutionLogsRequest
            {
                Logs = [new ExecutionLogItem { StepKey = from, Kind = string.IsNullOrWhiteSpace(kind) ? ExecutionLogKind.Progress : kind, Message = message }]
            }, ct);
        await plans.UpdateStepAsync(planId, from, new UpdateExecutionStepRequest { Status = ExecutionStatus.Completed }, actor, ct);
        if (string.IsNullOrWhiteSpace(to) || to == "-")
            return $"OK {from} -> completed";
        await plans.UpdateStepAsync(planId, to, new UpdateExecutionStepRequest { Status = ExecutionStatus.Running }, actor, ct);
        return $"OK {from} -> completed · {to} -> running";
    });

    [McpServerTool(Name = "prmake_block")]
    [Description("Trava a etapa esperando o USUARIO (permissao, VPN, credencial, acesso): o texto diz exatamente o que ele precisa fazer (o que liberar, o comando e a alternativa) — aparece no PRMake e no sino. Nunca deixe a etapa 'running' parada.")]
    public Task<string> Block(string card, string key, [Description("O que o usuario precisa fazer para destravar")] string text, string? phase = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        if (string.IsNullOrWhiteSpace(text)) throw new McpException("Diga o que o usuario precisa fazer para destravar a etapa.");
        var reason = text.Trim().Length > 1000 ? text.Trim()[..1000] : text.Trim();
        var planId = await PlanIdAsync(card, phase, ct);
        await plans.UpdateStepAsync(planId, key, new UpdateExecutionStepRequest
        {
            Status = ExecutionStatus.Waiting, WaitingOn = ExecutionWaitingOn.User, Reason = reason
        }, await ActorAsync(ct), ct);
        await plans.AppendLogsAsync(planId, new AppendExecutionLogsRequest
        {
            Logs = [new ExecutionLogItem { StepKey = key, Kind = ExecutionLogKind.Warning, Message = $"Aguardando voce: {reason}" }]
        }, ct);
        return $"OK {key} -> aguardando o usuario. Diga o mesmo no chat. No executor do PRMake, encerre a vez (ele retoma quando o usuario clicar 'Ja resolvi'); no terminal, deixe o vigia em segundo plano (prmake-plan.sh watch {card}).";
    });

    [McpServerTool(Name = "prmake_unblock")]
    [Description("A etapa travada volta a andar (o usuario resolveu pelo chat).")]
    public Task<string> Unblock(string card, string key, string? phase = null, CancellationToken ct = default) => Safe(async () =>
    {
        await plans.UpdateStepAsync(await PlanIdAsync(card, phase, ct), key, new UpdateExecutionStepRequest { Status = ExecutionStatus.Running }, await ActorAsync(ct), ct);
        return $"OK {key} -> running";
    });

    [McpServerTool(Name = "prmake_checkpoint")]
    [Description("Guarda onde parou na etapa (para retomar depois sem refazer).")]
    public Task<string> Checkpoint(string card, string key, string text, string? phase = null, CancellationToken ct = default) => Safe(async () =>
    {
        await plans.UpdateStepAsync(await PlanIdAsync(card, phase, ct), key, new UpdateExecutionStepRequest { Checkpoint = text }, await ActorAsync(ct), ct);
        return $"OK checkpoint {key}";
    });

    [McpServerTool(Name = "prmake_answer")]
    [Description("Grava a resposta que o usuario deu NO CHAT para uma pergunta do plano (numero da pergunta, 1 = primeira, ou o id).")]
    public Task<string> Answer(string card, [Description("Numero (1, 2...) ou id da pergunta")] string question, string text, string? phase = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        if (string.IsNullOrWhiteSpace(text)) throw new McpException("Resposta vazia.");
        var planId = await PlanIdAsync(card, phase, ct);
        Guid questionId;
        if (int.TryParse(question.Trim().TrimStart('#'), out var n))
        {
            var plan = await plans.GetAsync(planId, ct);
            var ordered = plan.Questions.OrderBy(q => q.Order).ToList();
            if (n < 1 || n > ordered.Count) throw new McpException($"Pergunta {n} nao existe (o plano tem {ordered.Count}).");
            questionId = ordered[n - 1].Id;
        }
        else if (!Guid.TryParse(question, out questionId))
            throw new McpException("Informe o numero ou o id da pergunta.");
        await plans.AnswerAsync(planId, questionId, text.Trim(), await ActorAsync(ct), ct);
        return "OK resposta gravada (via claude)";
    });

    [McpServerTool(Name = "prmake_file")]
    [Description("Grava um arquivo de texto nos arquivos do plano (script .sql com rollback, analise .md, texto do chamado, dados .csv/.json) " +
                 "direto pelo PRMake, sem depender da pasta local do card. Mesmo nome + tipo substitui a versao anterior. " +
                 "Use para todo script/analise/chamado que o usuario precisa ver no plano. " +
                 "Analise (phase analysis): so consultas somente leitura (00_consulta-<assunto>.sql), analise .md e dados. " +
                 "Correcao (phase correction): script que altera dados (com rollback), validacao e o texto do chamado " +
                 "(chamado-<nome>.md, kind ticket), com key = a etapa do chamado.")]
    public Task<string> SaveFile(string card,
        [Description("Nome do arquivo com extensao, ex.: 01_mover_usuario.sql, analise-inicial.md, chamado.md")] string name,
        [Description("Conteudo completo do arquivo (texto UTF-8)")] string content,
        [Description("script | analysis | data | image | attachment | ticket (padrao: pela extensao; chamado*.md = ticket)")] string? kind = null,
        [Description("Chave da etapa a que o arquivo pertence (opcional)")] string? key = null,
        [Description("Descricao curta (opcional)")] string? description = null,
        [Description("analysis | correction (padrao: o plano aberto mais recente)")] string? phase = null, CancellationToken ct = default) => Safe(async () =>
    {
        if (string.IsNullOrWhiteSpace(name)) throw new McpException("Informe o nome do arquivo (com extensao).");
        if (string.IsNullOrEmpty(content)) throw new McpException("Conteudo vazio.");
        var fileName = Path.GetFileName(name.Trim().Replace('\\', '/'));
        var upload = new ExecutionArtifactUpload(fileName, kind, string.IsNullOrWhiteSpace(key) ? null : key.Trim(), description,
            solvace.prform.Controllers.ExecutionPlanController.ResolveContentType(fileName, "text/plain; charset=utf-8"),
            Encoding.UTF8.GetBytes(content));
        var artifact = await plans.UploadArtifactAsync(await PlanIdAsync(card, phase, ct), upload, await ActorAsync(ct), ct);
        return Serialize(new { artifact.Id, artifact.Number, artifact.Name, artifact.Kind, artifact.Size, artifact.StepKey });
    });

    [McpServerTool(Name = "prmake_link")]
    [Description("Anexa um link a etapa: chamado (kind ticket; blocks=true trava a etapa ate ser resolvido), PR, documento.")]
    public Task<string> Link(string card, string key, string url, string? title = null,
        [Description("ticket | pr | doc | other (padrao: pela URL)")] string? kind = null,
        [Description("O chamado trava a etapa ate ser resolvido")] bool blocks = false, string? phase = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var link = await plans.AddLinkAsync(await PlanIdAsync(card, phase, ct), key, new AddExecutionLinkRequest
        {
            Url = url, Title = title, Kind = kind, BlocksStep = blocks
        }, await ActorAsync(ct), ct);
        return Serialize(new { link.Id, link.Kind, link.Status, link.BlocksStep });
    });

    [McpServerTool(Name = "prmake_correction")]
    [Description("Cria o plano de correcao do card a partir do plano de analise (equivale a prmake-plan.sh correction). Herda a sessao do Claude. Depois, o 'phase' padrao das ferramentas passa a ser a correcao; se usar o script, rode antes: prmake-plan.sh use <card> correction.")]
    public Task<string> Correction(string card, string title,
        [Description("Etapas: [{ key, title, description?, executor?, kind?, repository?, dependsOn? }]")] List<ExecutionStepDefinition> steps,
        CancellationToken ct = default) => Safe(async () =>
    {
        var analysis = (await plans.GetByCardAsync(card.Trim(), ct)).FirstOrDefault(p => p.Phase == ExecutionPhase.Analysis)
                       ?? throw new McpException($"O card {card} nao tem plano de analise.");
        var plan = await plans.CreateAsync(new CreateExecutionPlanRequest
        {
            CardNumber = card.Trim(), Kind = "analisar-bug", Title = title, Phase = ExecutionPhase.Correction,
            ParentPlanId = analysis.Id, Steps = steps
        }, await ActorAsync(ct), ct);
        return Serialize(new
        {
            plan.Id, plan.Phase, plan.Status,
            steps = plan.Steps.OrderBy(s => s.Order).Select(s => new { s.Key, s.Status, s.Executor, s.Kind }),
            script = $"prmake-plan.sh use {card.Trim()} correction"
        });
    });

    // ── Internos ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Erro de regra (etapa inexistente, plano cancelado, DevOps recusou...) vai com a mensagem para o Claude.</summary>
    private static async Task<T> Safe<T>(Func<Task<T>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception e) when (e is DomainException or ExecutionPlanNotFoundException or ExecutionForbiddenException
                                       or ExecutionPlanConcurrencyException or solvace.azure.domain.Exceptions.DevOpsActionException
                                       or solvace.prform.application.UserIntegrations.PersonalIntegrationRequiredException
                                       or InvalidOperationException)
        {
            // InvalidOperation: integração do Azure ausente ("Plugin ... não encontrado") — a mensagem diz o que configurar.
            throw new McpException(e.Message, e);
        }
    }


    private async Task<Guid> PlanIdAsync(string card, string? phase, CancellationToken ct)
    {
        var summaries = await plans.GetByCardAsync(card.Trim(), ct);
        if (summaries.Count == 0)
            throw new McpException($"O card {card} ainda nao tem plano — crie com prmake-plan.sh start/contexto.");
        var p = phase?.Trim().ToLowerInvariant();
        var candidates = string.IsNullOrEmpty(p) ? summaries : summaries.Where(s => s.Phase == p).ToList();
        if (candidates.Count == 0)
            throw new McpException($"O card {card} nao tem plano de {p}.");
        var open = candidates.FirstOrDefault(s => s.Status is not (ExecutionStatus.Completed or ExecutionStatus.Cancelled));
        return (open ?? candidates[0]).Id;
    }

    private async Task<ExecutionActor> ActorAsync(CancellationToken ct)
    {
        var user = http.HttpContext?.User ?? new ClaimsPrincipal();
        Guid? userId = Guid.TryParse(user.FindFirst("ExternalId")?.Value, out var id) ? id : null;
        string? name = userId.HasValue ? await users.GetFullNameAsync(userId.Value, ct) : null;
        name ??= user.FindFirst(ClaimTypes.Name)?.Value;
        return new ExecutionActor(userId, string.IsNullOrWhiteSpace(name) ? "Claude" : name.Trim(), true);
    }

    private async Task<bool> QaValidatedAsync(string card, CancellationToken ct)
    {
        foreach (var summary in await plans.GetByCardAsync(card, ct))
        {
            var plan = await plans.GetAsync(summary.Id, ct);
            if (plan.Steps.Any(s => s.Key == "validar-qa" && s.Status == ExecutionStatus.Completed))
                return true;
        }
        return false;
    }

    [GeneratedRegex(@"^(imagem|image|img|anexo|arquivo)\s*#?(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex OrdinalRef();

    private static ExecutionArtifactResponse? Resolve(List<ExecutionArtifactResponse> all, string reference)
    {
        var r = reference.Trim();
        var m = OrdinalRef().Match(r);
        if (m.Success)
        {
            var n = int.Parse(m.Groups[2].Value);
            var pool = m.Groups[1].Value.StartsWith("im", StringComparison.OrdinalIgnoreCase)
                ? all.Where(a => a.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)).ToList()
                : all;
            // "imagem 2" = a 2ª imagem; se não houver, tenta como número do anexo.
            return n >= 1 && n <= pool.Count ? pool[n - 1] : all.FirstOrDefault(a => a.Number == n);
        }
        if (int.TryParse(r.TrimStart('#'), out var number))
            return all.FirstOrDefault(a => a.Number == number);
        return all.LastOrDefault(a => string.Equals(a.Name, r, StringComparison.OrdinalIgnoreCase))
               ?? all.LastOrDefault(a => a.Name.Contains(r, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeText(string contentType, byte[] data) =>
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
        || (data.Length < 2_000_000 && !data.Take(4096).Contains((byte)0));

    private static string? StripHtml(string? html) =>
        string.IsNullOrEmpty(html) ? html
            : System.Net.WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, @"<(br|/p|/div|/li)[^>]*>", "\n", RegexOptions.IgnoreCase), "<.*?>", string.Empty)).Trim();

    private static string? Trim(string? value, int max) => value is null || value.Length <= max ? value : value[..max] + " (...)";

    private static CallToolResult Error(string message) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}
