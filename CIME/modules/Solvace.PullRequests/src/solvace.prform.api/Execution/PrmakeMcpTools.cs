using System.ComponentModel;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using solvace.azure.application.Contract;
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
    IDevOpsActionsService devops,
    IHttpContextAccessor http,
    solvace.timeline.application.Contracts.IUserRepository users)
{
    public const string Instructions =
        "PRMake: plano de execucao do card (etapas, andamento, perguntas ao usuario, comentarios e anexos, Timeline, " +
        "mover o card no DevOps). Toda gravacao no PRMake passa por aqui ou pelo prmake-plan.sh. Informe sempre o card; " +
        "'phase' (analysis|correction) escolhe o plano quando o card tem os dois (padrao: o plano aberto mais recente).";

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
            steps = plan.Steps.OrderBy(s => s.Order).Select(s => new { s.Key, s.Title, s.Status, s.Executor, s.Kind, s.WaitingOn, reason = s.StatusReason, s.DependsOn }),
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
            c.OpenQuestions, c.UserPending, c.LastUserNoteNumber
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
    [Description("Pergunta ao usuario pela tela do card (a etapa fica aguardando a resposta). Cada opcao: { label (a propria opcao), description?, recommended? }.")]
    public Task<string> Ask(
        string card,
        [Description("Perguntas: [{ stepKey?, text, options?: [{ label, description?, recommended? }], allowFreeText? }]")] List<ExecutionQuestionItem> questions,
        string? phase = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var created = await plans.AskAsync(await PlanIdAsync(card, phase, ct), new AskExecutionQuestionsRequest { Questions = questions }, await ActorAsync(ct), ct);
        return Serialize(created.Select(q => new { q.Id, q.Order, q.Status }));
    });

    [McpServerTool(Name = "prmake_answers", ReadOnly = true)]
    [Description("Perguntas do plano com as respostas (de quem, por onde).")]
    public Task<string> Answers(string card, string? phase = null, CancellationToken ct = default) => Safe(async () =>
    {
        var plan = await plans.GetAsync(await PlanIdAsync(card, phase, ct), ct);
        return Serialize(plan.Questions.OrderBy(q => q.Order).Select(q => new
        {
            q.Order, q.StepKey, q.Text, q.Status, q.Answer, q.AnsweredBy, q.AnsweredVia
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
    [Description("Move/atualiza o card no Azure DevOps pelas regras configuradas no PRMake. action: dev-test-in-qa | ready-for-qa | test-in-production | zero-remaining | initial-estimate. ready-for-qa so com a etapa validar-qa concluida pelo usuario ou com 'authorization' (quem autorizou e quando, explicitamente).")]
    public Task<string> DevOps(string card, string action, [Description("Autorizacao explicita do usuario para ready-for-qa")] string? authorization = null,
        CancellationToken ct = default) => Safe(async () =>
    {
        var id = card.Trim();
        switch (action.Trim().ToLowerInvariant())
        {
            case "dev-test-in-qa": return Serialize(await devops.MoveToDevTestInQaAsync(id, ct));
            case "test-in-production": return Serialize(await devops.MoveToTestInProductionAsync(id, ct));
            case "zero-remaining": return Serialize(await devops.ZeroRemainingAsync(id, ct));
            case "initial-estimate": return Serialize(await devops.SetInitialEstimateAsync(id, ct));
            case "ready-for-qa":
                if (string.IsNullOrWhiteSpace(authorization) && !await QaValidatedAsync(id, ct))
                    throw new McpException("Ready for QA so com a etapa validar-qa concluida pelo usuario no plano ou com a autorizacao explicita dele (informe em 'authorization').");
                return Serialize(await devops.MoveToReadyForQaAsync(id, ct));
            default:
                throw new McpException($"Acao desconhecida: {action}");
        }
    });

    [McpServerTool(Name = "prmake_queue", ReadOnly = true)]
    [Description("Pedido do executor no card (na fila, rodando, falhou) e os ultimos pedidos.")]
    public Task<string> Queue(string card, CancellationToken ct = default) => Safe(async () =>
    {
        var state = await queue.GetCardAsync(card.Trim(), null, ct);
        return Serialize(new
        {
            active = state.Active is { } a ? new { a.Id, a.Kind, a.Status, a.WorkerName, a.Attempts, a.WaitReason, a.LastError } : null,
            recent = state.Recent.Select(r => new { r.Id, r.Kind, r.Status, r.FinishedReason, r.LastError, r.CostUsd })
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
                                       or ExecutionPlanConcurrencyException or solvace.azure.domain.Exceptions.DevOpsActionException)
        {
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

    private static CallToolResult Error(string message) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}
