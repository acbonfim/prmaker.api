using solvace.executionplans.domain.Requests;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Plano de execução de uma skill (ex.: analisar-bug) sobre um card: as etapas, o status geral e
/// o sinal de vida (heartbeat) de quem executa. Toda regra de transição de status vive aqui.
/// </summary>
public class ExecutionPlan
{
    public const int MaxTitleLength = 300;
    public const int MaxSummaryLength = 20_000;

    public Guid Id { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public string Kind { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Summary { get; private set; }

    public string Status { get; private set; } = ExecutionStatus.Pending;
    public string? StatusReason { get; private set; }
    public string? StatusChangedBy { get; private set; }

    public Guid? CreatedByUserId { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Última chamada da skill. Sem sinal por muito tempo = a sessão provavelmente caiu.</summary>
    public DateTimeOffset? LastActivityAt { get; private set; }

    /// <summary>Token de concorrência (xmin do PostgreSQL): usuário e skill mexendo ao mesmo tempo não se atropelam.</summary>
    public uint Version { get; private set; }

    public List<ExecutionStep> Steps { get; private set; } = [];

    /// <summary>analysis | correction (0024).</summary>
    public string Phase { get; private set; } = ExecutionPhase.Analysis;

    /// <summary>Plano de análise que originou este plano de correção (0024).</summary>
    public Guid? ParentPlanId { get; private set; }

    /// <summary>Sessões do Claude Code que trabalharam no plano (0033) — a última vista é a que se retoma.</summary>
    public List<ExecutionSession> Sessions { get; private set; } = [];

    /// <summary>Pedido de "continuar" feito na tela (0033): o vigia local retoma a sessão; a skill confirma ao pegar.</summary>
    public DateTimeOffset? ResumeRequestedAt { get; private set; }
    public string? ResumeRequestedBy { get; private set; }
    public DateTimeOffset? ResumeHandledAt { get; private set; }

    /// <summary>
    /// Até qual comentário do card a skill já leu (0037) e quando — a tela mostra "o Claude leu e está analisando".
    /// Gravado direto no banco quando a skill busca os comentários (não passa pelo agregado).
    /// </summary>
    public int? NotesReadNumber { get; private set; }
    public DateTimeOffset? NotesReadAt { get; private set; }

    public const int MaxSessions = 50;

    /// <summary>
    /// 0075: contexto da passagem (handoff) — o que a skill deixa escrito ao entrar em espera (o que foi feito, o que se
    /// espera, riscos, como retomar). Vai no resumo "Situação atual" do card junto com os fatos do PRMake.
    /// </summary>
    public string? Handoff { get; private set; }
    public DateTimeOffset? HandoffAt { get; private set; }
    public string? HandoffBy { get; private set; }
    public const int MaxHandoffLength = 8_000;

    /// <summary>0080: divergência entre a análise e a base real (log <c>divergence</c>), pendente até alguém confirmar.</summary>
    public string? DivergenceText { get; private set; }
    public string? DivergenceStepKey { get; private set; }
    public DateTimeOffset? DivergenceAt { get; private set; }
    public string? DivergenceConfirmedBy { get; private set; }
    public DateTimeOffset? DivergenceConfirmedAt { get; private set; }
    public const int MaxDivergenceLength = 4_000;

    public bool DivergencePending => DivergenceAt is not null && DivergenceConfirmedAt is null;

    public void RaiseDivergence(string text, string? stepKey, DateTimeOffset now)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return;
        DivergenceText = t.Length <= MaxDivergenceLength ? t : t[..MaxDivergenceLength];
        DivergenceStepKey = string.IsNullOrWhiteSpace(stepKey) ? null : ExecutionStep.NormalizeKey(stepKey);
        DivergenceAt = now;
        DivergenceConfirmedBy = null;
        DivergenceConfirmedAt = null;
        UpdatedAt = now;
    }

    public void ConfirmDivergence(string by, DateTimeOffset now)
    {
        if (!DivergencePending)
            throw new DomainException("Não há divergência pendente neste plano.");
        DivergenceConfirmedBy = Clean(by, 200);
        DivergenceConfirmedAt = now;
        UpdatedAt = now;
    }

    /// <summary>0075: último tipo de situação registrado (a Timeline ganha uma entrada quando muda). Gravado direto no banco.</summary>
    public string? SituationKind { get; private set; }
    public DateTimeOffset? SituationAt { get; private set; }

    /// <summary>0075: o PRMake moveu o card para Dev Test in QA (PR da branch de QA mesclado) — uma vez só.</summary>
    public DateTimeOffset? DevTestInQaAt { get; private set; }
    /// <summary>0075: última tentativa e o erro dela (sem integração, 409...) — nova tentativa no máximo a cada 10 min.</summary>
    public DateTimeOffset? DevTestInQaTriedAt { get; private set; }
    public string? DevTestInQaError { get; private set; }
    public const int MaxDevTestInQaErrorLength = 500;

    /// <summary>
    /// 0076: última importação das evidências do card no DevOps (anexos, imagens da descrição e da discussion) e o erro
    /// dela; ou o motivo, dado pelo usuário, para seguir a análise sem elas.
    /// </summary>
    public DateTimeOffset? EvidenceCheckedAt { get; private set; }
    public string? EvidenceError { get; private set; }
    public string? EvidenceSkipReason { get; private set; }
    public const int MaxEvidenceTextLength = 500;

    public void RecordEvidenceCheck(string? error, DateTimeOffset now)
    {
        if (error is null)
        {
            EvidenceCheckedAt = now;
            EvidenceError = null;
        }
        else
        {
            EvidenceError = Clean(error, MaxEvidenceTextLength);
        }
        UpdatedAt = now;
    }

    public void SkipEvidence(string reason, string by, DateTimeOffset now)
    {
        var r = Clean(reason, MaxEvidenceTextLength) ?? throw new DomainException("Diga por que a análise segue sem as evidências (o que o usuário respondeu).");
        EvidenceSkipReason = Clean($"{r} — {by}", MaxEvidenceTextLength);
        UpdatedAt = now;
    }

    /// <summary>0075: comentário da situação publicado na discussion do DevOps (republicar atualiza o mesmo).</summary>
    public int? SituationCommentId { get; private set; }

    /// <summary>0076: o que foi publicado na discussion (markdown), quando e por quem — a tela mostra e compara com o atual.</summary>
    public string? SituationPostedText { get; private set; }
    public DateTimeOffset? SituationPostedAt { get; private set; }
    public string? SituationPostedBy { get; private set; }

    /// <summary>0078: só este card — publicar sozinho os resumos de espera? null = preferência do dono / padrão global.</summary>
    public bool? SituationAutoPost { get; private set; }

    public void SetSituationAutoPost(bool? value, DateTimeOffset now)
    {
        SituationAutoPost = value;
        UpdatedAt = now;
    }

    public void SetSituationComment(int commentId, DateTimeOffset now)
    {
        SituationCommentId = commentId;
        UpdatedAt = now;
    }

    public ExecutionSession? CurrentSession => Sessions.OrderByDescending(s => s.LastSeenAt).FirstOrDefault();

    public bool ResumePending => ResumeRequestedAt is { } requested && (ResumeHandledAt is null || ResumeHandledAt < requested);

    /// <summary>A skill informa em qual sessão/máquina/pasta está trabalhando (no start, ao retomar e no heartbeat).</summary>
    public ExecutionSession RegisterSession(string sessionId, string? host, string? cwd, DateTimeOffset now)
    {
        var id = Clean(sessionId, ExecutionSession.MaxSessionIdLength) ?? throw new DomainException("Informe o id da sessão do Claude Code.");
        var session = Sessions.FirstOrDefault(s => s.SessionId == id);
        if (session is null)
        {
            session = new ExecutionSession { SessionId = id, StartedAt = now };
            Sessions.Add(session);
            if (Sessions.Count > MaxSessions)
                Sessions.Remove(Sessions.OrderBy(s => s.LastSeenAt).First());
        }
        session.Host = Clean(host, ExecutionSession.MaxHostLength) ?? session.Host;
        session.Cwd = Clean(cwd, ExecutionSession.MaxCwdLength) ?? session.Cwd;
        session.LastSeenAt = now;
        return session;
    }

    /// <summary>Custo acumulado da sessão (a skill manda o total lido do transcript; o último valor vence).</summary>
    public void RecordUsage(string sessionId, string? host, int turns, long input, long output, long cacheRead, long cacheWrite, string? model, DateTimeOffset now,
        int? mcpCalls = null, int? scriptCalls = null, int? kbCalls = null, int? searchCalls = null, IReadOnlyCollection<ExecutionModelUsage>? models = null,
        IReadOnlyCollection<ExecutionReadSource>? sources = null, IReadOnlyCollection<ExecutionExploredFile>? exploredFiles = null)
    {
        if (turns < 0 || input < 0 || output < 0 || cacheRead < 0 || cacheWrite < 0)
            throw new DomainException("Valores de uso inválidos.");
        var session = RegisterSession(sessionId, host, null, now);
        session.Turns = turns;
        session.InputTokens = input;
        session.OutputTokens = output;
        session.CacheReadTokens = cacheRead;
        session.CacheWriteTokens = cacheWrite;
        session.Model = Clean(model, 100) ?? session.Model;
        if (mcpCalls is >= 0) session.McpCalls = mcpCalls;
        if (scriptCalls is >= 0) session.ScriptCalls = scriptCalls;
        if (kbCalls is >= 0) session.KbCalls = kbCalls;
        if (searchCalls is >= 0) session.SearchCalls = searchCalls;
        // 0047: por modelo (acumulado, como os totais). Quem não manda (skill/executor antigos) mantém o que havia.
        if (models is { Count: > 0 })
        {
            if (models.Any(m => m.Turns < 0 || m.InputTokens < 0 || m.OutputTokens < 0 || m.CacheReadTokens < 0 || m.CacheWriteTokens < 0))
                throw new DomainException("Valores de uso inválidos.");
            session.Models = ExecutionModelUsage.Sum(models
                .Where(m => !string.IsNullOrWhiteSpace(m.Model))
                .Select(m => { var c = m.Copy(); c.Model = Clean(m.Model, ExecutionModelUsage.MaxModelLength)!; return c; }));
        }
        // 0055: de onde leu. Quem não manda (skill/executor antigos) mantém o que havia.
        if (sources is not null)
        {
            if (sources.Any(s => s.Calls < 0 || s.Tokens < 0) || exploredFiles?.Any(f => f.Reads < 0 || f.Tokens < 0) == true)
                throw new DomainException("Valores de uso inválidos.");
            session.Sources = ExecutionReadSource.Sum(sources.Select(s => { var c = s.Copy(); c.Key = (s.Key ?? "").Trim().ToLowerInvariant(); return c; }));
            session.ExploredFiles = ExecutionExploredFile.Top((exploredFiles ?? [])
                .Where(f => !string.IsNullOrWhiteSpace(f.Path))
                .Select(f => { var c = f.Copy(); c.Path = Clean(f.Path, ExecutionExploredFile.MaxPathLength)!; return c; }));
        }
        session.UsageUpdatedAt = now;
    }

    /// <summary>
    /// 0044: linha de base da sessão = o que ela já registrava no plano pai (análise → correção na mesma sessão).
    /// Só a primeira vez; sessão que não veio do pai fica com base zero.
    /// </summary>
    public void SetSessionBaseline(string sessionId, ExecutionSession? fromParent, DateTimeOffset now)
    {
        var session = RegisterSession(sessionId, null, null, now);
        if (session.BaselineSet) return;
        session.BaselineSet = true;
        if (fromParent is null) return;
        session.BaseTurns = fromParent.Turns;
        session.BaseInputTokens = fromParent.InputTokens;
        session.BaseOutputTokens = fromParent.OutputTokens;
        session.BaseCacheReadTokens = fromParent.CacheReadTokens;
        session.BaseCacheWriteTokens = fromParent.CacheWriteTokens;
        session.BaseMcpCalls = fromParent.McpCalls ?? 0;
        session.BaseScriptCalls = fromParent.ScriptCalls ?? 0;
        session.BaseKbCalls = fromParent.KbCalls ?? 0;
        session.BaseSearchCalls = fromParent.SearchCalls ?? 0;
        session.BaseSources = fromParent.Sources.Select(s => s.Copy()).ToList();
        session.BaseExploredFiles = fromParent.ExploredFiles.Select(f => f.Copy()).ToList();
        // 0047: por modelo. Pai antigo (sem modelos) mas com consumo: tudo no modelo dele — senão a correção somaria a análise.
        session.BaseModels = fromParent.Models.Count > 0
            ? fromParent.Models.Select(m => m.Copy()).ToList()
            : fromParent.Model is { Length: > 0 } pm && fromParent.Turns > 0
                ? [new ExecutionModelUsage
                {
                    Model = pm, Turns = fromParent.Turns, InputTokens = fromParent.InputTokens, OutputTokens = fromParent.OutputTokens,
                    CacheReadTokens = fromParent.CacheReadTokens, CacheWriteTokens = fromParent.CacheWriteTokens
                }]
                : [];
    }

    /// <summary>
    /// 0047: consumo líquido do plano por modelo. Sessão antiga (sem modelos) entra inteira no modelo dela; sem modelo
    /// conhecido, a lista fica vazia e a tela usa só o total.
    /// </summary>
    public List<ExecutionModelUsage> NetModelUsage() => NetModelUsageOf(Sessions);

    public static List<ExecutionModelUsage> NetModelUsageOf(IEnumerable<ExecutionSession> sessions)
    {
        var parts = new List<ExecutionModelUsage>();
        foreach (var s in sessions)
        {
            if (s.Models.Count > 0) { parts.AddRange(s.NetModels()); continue; }
            if (s.Model is not { Length: > 0 } || s.NetTurns() == 0) continue;
            parts.Add(new ExecutionModelUsage
            {
                Model = s.Model, Turns = s.NetTurns(), InputTokens = s.NetInputTokens(), OutputTokens = s.NetOutputTokens(),
                CacheReadTokens = s.NetCacheReadTokens(), CacheWriteTokens = s.NetCacheWriteTokens()
            });
        }
        return ExecutionModelUsage.Sum(parts);
    }

    /// <summary>0055: leituras líquidas do plano por origem e os arquivos de código mais explorados.</summary>
    public List<ExecutionReadSource> NetReadSources() => ExecutionReadSource.Sum(Sessions.SelectMany(s => s.NetSources()));
    public List<ExecutionExploredFile> NetExploredFiles() => ExecutionExploredFile.Top(Sessions.SelectMany(s => s.NetExploredFiles()));

    /// <summary>Modelo da sessão mais recente que informou consumo (para estimar o custo na tela).</summary>
    public string? UsageModel => Sessions.Where(s => s.Model != null).OrderByDescending(s => s.UsageUpdatedAt ?? s.LastSeenAt).FirstOrDefault()?.Model;

    public void RequestResume(string actor, DateTimeOffset now)
    {
        if (IsFinished) throw new DomainException("O plano já terminou — não há o que continuar.");
        if (CurrentSession is null)
            throw new DomainException("Nenhuma sessão do Claude registrada neste plano — retome com prmake-card no terminal.");
        ResumeRequestedAt = now;
        ResumeRequestedBy = actor;
        UpdatedAt = now;
    }

    public void AcknowledgeResume(DateTimeOffset now)
    {
        ResumeHandledAt = now;
        UpdatedAt = now;
    }

    private static string? Clean(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= max ? v : v[..max];
    }

    protected ExecutionPlan() { }

    public ExecutionPlan(string cardNumber, string kind, string title, string? summary, Guid? userId, string userName, DateTimeOffset now,
        string? phase = null, Guid? parentPlanId = null)
    {
        var normalizedPhase = string.IsNullOrWhiteSpace(phase) ? ExecutionPhase.Analysis : phase.Trim().ToLowerInvariant();
        if (!ExecutionPhase.All.Contains(normalizedPhase))
            throw new DomainException($"Fase inválida: '{phase}' (use analysis ou correction).");
        if (normalizedPhase == ExecutionPhase.Correction && parentPlanId is null)
            throw new DomainException("O plano de correção precisa do plano de análise de origem (parentPlanId).");
        Phase = normalizedPhase;
        ParentPlanId = normalizedPhase == ExecutionPhase.Correction ? parentPlanId : null;

        if (string.IsNullOrWhiteSpace(cardNumber))
            throw new DomainException("O número do card é obrigatório.");
        if (string.IsNullOrWhiteSpace(userName))
            throw new DomainException("O nome de quem criou o plano é obrigatório.");

        Id = Guid.NewGuid();
        CardNumber = cardNumber.Trim();
        Kind = string.IsNullOrWhiteSpace(kind) ? "analisar-bug" : kind.Trim().ToLowerInvariant();
        SetTitle(title);
        SetSummary(summary);
        CreatedByUserId = userId;
        CreatedBy = userName.Trim();
        CreatedAt = now;
        UpdatedAt = now;
        LastActivityAt = now;
    }

    /// <summary>Completed/cancelled: o plano terminou. Só "completed" pode ser reaberto (pela skill).</summary>
    public bool IsFinished => Status is ExecutionStatus.Completed or ExecutionStatus.Cancelled;

    /// <summary>0075: grava o contexto da passagem (texto vazio limpa).</summary>
    public void SetHandoff(string? text, string by, DateTimeOffset now)
    {
        var t = text?.Trim();
        if (t is { Length: > MaxHandoffLength })
            throw new DomainException($"O resumo da passagem pode ter no máximo {MaxHandoffLength} caracteres.");
        Handoff = string.IsNullOrEmpty(t) ? null : t;
        HandoffAt = Handoff is null ? null : now;
        HandoffBy = Handoff is null ? null : Clean(by, 200);
        UpdatedAt = now;
    }

    /// <summary>0075: resultado da tentativa de mover para Dev Test in QA (erro null = movido).</summary>
    public void RecordDevTestInQa(string? error, DateTimeOffset now)
    {
        DevTestInQaTriedAt = now;
        DevTestInQaError = Clean(error, MaxDevTestInQaErrorLength);
        if (error is null)
            DevTestInQaAt = now;
    }

    /// <summary>0075: pode tentar mover para Dev Test in QA agora? (nunca movido e sem tentativa nos últimos 10 min).</summary>
    public bool DevTestInQaDue(DateTimeOffset now) =>
        DevTestInQaAt is null && (DevTestInQaTriedAt is null || now - DevTestInQaTriedAt >= TimeSpan.FromMinutes(10));

    /// <summary>0075: etapa de validação em QA (key validar-qa ou validar-qa-*), se o plano tiver.</summary>
    public ExecutionStep? QaValidationStep =>
        Steps.Where(s => s.Key == "validar-qa" || s.Key.StartsWith("validar-qa-", StringComparison.Ordinal))
            .OrderBy(s => ExecutionStatus.IsStepFinished(s.Status) ? 1 : 0).ThenBy(s => s.Order).FirstOrDefault();

    public void SetTitle(string? title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new DomainException("O título do plano é obrigatório.");
        Title = trimmed.Length <= MaxTitleLength ? trimmed : trimmed[..MaxTitleLength];
    }

    private void SetSummary(string? summary)
    {
        var trimmed = summary?.Trim();
        if (trimmed is { Length: > MaxSummaryLength })
            throw new DomainException($"O resumo pode ter no máximo {MaxSummaryLength} caracteres.");
        if (!string.IsNullOrEmpty(trimmed))
            Summary = trimmed;
    }

    /// <summary>Sinal de vida da skill (toda chamada dela passa por aqui).</summary>
    public void Touch(DateTimeOffset now, bool fromExecutor)
    {
        UpdatedAt = now;
        if (fromExecutor)
            LastActivityAt = now;
    }

    /// <summary>
    /// Define/refina as etapas (upsert pela chave), na ordem enviada. Etapas que sumiram da lista:
    /// se ainda pendentes, saem do plano; se já começaram, ficam (histórico) depois das enviadas. Etapa pedida por uma
    /// pessoa (<see cref="ExecutionStep.RequestedKeyPrefix"/>, <see cref="AddStep"/>) não sai por omissão: fica antes da
    /// etapa que vinha depois dela.
    /// Card 78728: <paramref name="replace"/> = false e a lista não traz todas as etapas → é um ajuste parcial (ex.: trocar
    /// o <c>dependsOn</c> de uma etapa): as enviadas mudam no lugar, as novas entram depois da enviada anterior e nenhuma sai
    /// — antes o ajuste de uma etapa apagava <c>validar-qa</c>/<c>fechar-card</c> e o plano concluía sem a validação.
    /// </summary>
    public void UpsertSteps(IReadOnlyList<ExecutionStepDefinition> definitions, DateTimeOffset now, bool replace = true)
    {
        EnsureNotCancelled();

        if (!replace && Steps.Any(s => definitions.All(d => ExecutionStep.NormalizeKey(d.Key) != s.Key)))
        {
            PatchSteps(definitions, now);
            return;
        }

        var previous = Steps.OrderBy(s => s.Order).Select(s => s.Key).ToList();
        var seen = new HashSet<string>();
        var order = 0;
        foreach (var def in definitions)
        {
            var key = ExecutionStep.NormalizeKey(def.Key);
            if (!seen.Add(key))
                throw new DomainException($"Etapa repetida no plano: '{key}'.");

            order += 10;
            var step = FindStep(key);
            if (step is null)
            {
                step = new ExecutionStep(Id, key, order, def.Title, def.Description, now);
                Steps.Add(step);
            }
            else
            {
                step.Order = order;
                if (!string.IsNullOrWhiteSpace(def.Title))
                    step.SetTitle(def.Title);
                if (def.Description is not null)
                    step.SetDescription(def.Description);
                step.Touch(now);
            }
            step.SetShape(def.Executor, def.Kind, def.Repository, def.DependsOn, def.TaskKind);
        }

        // Card 78680: etapa que a pessoa pôs no lugar (ainda não feita) fica onde ela pôs, mesmo a skill mandando outra ordem.
        var requested = Steps.Where(s => seen.Contains(s.Key) && s.OrderPinned && !ExecutionStatus.IsStepFinished(s.Status))
            .OrderBy(s => previous.IndexOf(s.Key)).ToList();
        foreach (var orphan in Steps.Where(s => !seen.Contains(s.Key)).OrderBy(s => s.Order).ToList())
        {
            if (orphan.Status == ExecutionStatus.Pending && (orphan.IsRequested || orphan.OrderPinned))
            {
                requested.Add(orphan);
            }
            else if (orphan.Status == ExecutionStatus.Pending)
            {
                Steps.Remove(orphan);
            }
            else
            {
                order += 10;
                orphan.Order = order;
            }
        }
        if (requested.Count > 0)
            KeepRequestedInPlace(requested, previous);

        Touch(now, fromExecutor: true);
    }

    /// <summary>Ajuste parcial (card 78728): muda só as etapas enviadas, sem tirar nem reordenar as outras.</summary>
    private void PatchSteps(IReadOnlyList<ExecutionStepDefinition> definitions, DateTimeOffset now)
    {
        var ordered = Steps.OrderBy(s => s.Order).ToList();
        var seen = new HashSet<string>();
        ExecutionStep? last = null;
        foreach (var def in definitions)
        {
            var key = ExecutionStep.NormalizeKey(def.Key);
            if (!seen.Add(key))
                throw new DomainException($"Etapa repetida no plano: '{key}'.");

            var step = FindStep(key);
            if (step is null)
            {
                step = new ExecutionStep(Id, key, 0, def.Title, def.Description, now);
                Steps.Add(step);
                ordered.Insert(last is null ? ordered.Count : ordered.IndexOf(last) + 1, step);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(def.Title))
                    step.SetTitle(def.Title);
                if (def.Description is not null)
                    step.SetDescription(def.Description);
                step.Touch(now);
            }
            step.SetShape(def.Executor, def.Kind, def.Repository, def.DependsOn, def.TaskKind);
            last = step;
        }

        var order = 0;
        foreach (var s in ordered)
            s.Order = order += 10;
        Touch(now, fromExecutor: true);
    }

    /// <summary>Etapas pedidas que a skill omitiu voltam para antes da etapa que vinha depois delas (ou para o fim).</summary>
    private void KeepRequestedInPlace(List<ExecutionStep> requested, List<string> previous)
    {
        var ordered = Steps.Except(requested).OrderBy(s => s.Order).ToList();
        foreach (var step in requested.OrderBy(s => previous.IndexOf(s.Key)))
        {
            var next = previous.Skip(previous.IndexOf(step.Key) + 1)
                .Select(k => ordered.FirstOrDefault(s => s.Key == k)).FirstOrDefault(s => s is not null);
            ordered.Insert(next is null ? ordered.Count : ordered.IndexOf(next), step);
        }
        var order = 0;
        foreach (var s in ordered)
            s.Order = order += 10;
    }

    /// <summary>
    /// Acrescenta uma etapa nova, pendente, a pedido de uma pessoa (Especialista do caso: "antes de continuar, verifique
    /// X") — sem mudar nem remover as que existem. Entra antes de <paramref name="beforeKey"/> (etapa ainda não terminada)
    /// ou, sem ele, antes da primeira etapa não terminada (a que está em andamento/espera); com tudo terminado, no fim.
    /// </summary>
    public ExecutionStep AddStep(ExecutionStepDefinition definition, string? beforeKey, DateTimeOffset now, string? position = null)
    {
        EnsureNotCancelled();
        if (IsFinished)
            throw new DomainException("O plano já terminou — não dá para acrescentar etapa. Crie um item de trabalho avulso.");
        var key = ExecutionStep.NormalizeKey(definition.Key);
        if (FindStep(key) is not null)
            throw new DomainException($"Já existe a etapa '{key}' no plano — escolha outra chave.");

        var ordered = Steps.OrderBy(s => s.Order).ToList();
        ExecutionStep? anchor;
        var last = string.Equals(position?.Trim(), TaskPosition.Last, StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(beforeKey))
        {
            // Card 78680: "no fim" = depois de todas; o padrão (e "first") é antes da primeira ainda não terminada.
            anchor = last ? null : ordered.FirstOrDefault(s => !ExecutionStatus.IsStepFinished(s.Status));
        }
        else
        {
            anchor = FindStep(ExecutionStep.NormalizeKey(beforeKey))
                     ?? throw new DomainException($"Etapa '{beforeKey}' não encontrada no plano.");
            if (ExecutionStatus.IsStepFinished(anchor.Status))
                throw new DomainException($"A etapa '{anchor.Key}' já terminou — a etapa nova entra antes de uma etapa ainda não terminada.");
        }

        var step = new ExecutionStep(Id, key, 0, definition.Title, definition.Description, now);
        step.SetShape(definition.Executor, definition.Kind, definition.Repository, definition.DependsOn, definition.TaskKind);
        step.OrderPinned = true;
        ordered.Insert(anchor is null ? ordered.Count : ordered.IndexOf(anchor), step);
        if (TaskOrdering.Violation(ordered, s => ExecutionStatus.IsStepFinished(s.Status), Dependencies) is { } v)
            throw new DomainException($"A etapa '{v.Task.Key}' depende de '{v.Dependency.Key}' — a etapa nova não pode ficar nessa posição.");
        Steps.Add(step);
        var order = 0;
        foreach (var s in ordered)
            s.Order = order += 10;

        Touch(now, fromExecutor: false);
        return step;
    }

    /// <summary>
    /// Card 78794: uma entrega já feita foi refeita (script recusado pela infra, chamado corrigido). A etapa nova entra
    /// logo depois da refeita, já concluída e dependendo dela. As ainda não terminadas que dependiam da refeita passam a
    /// depender também da nova e ganham um aviso no começo da instrução, para que o tratamento e a pessoa saibam que a
    /// entrega mudou. Sem dependentes declarados, o aviso vai para as não terminadas que vêm depois. Devolve a etapa
    /// nova e as avisadas.
    /// </summary>
    public (ExecutionStep Step, IReadOnlyList<ExecutionStep> Warned) RedoStep(string redoneKey, ExecutionStepDefinition definition,
        string reason, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();
        if (IsFinished)
            throw new DomainException("O plano já terminou — refaça a entrega num item de trabalho avulso.");
        var redone = RequireStep(redoneKey);
        if (redone.Status != ExecutionStatus.Completed)
            throw new DomainException($"A etapa '{redone.Key}' ainda não foi concluída ({redone.Status}) — ajuste a instrução dela em vez de refazer.");
        var why = reason?.Trim();
        if (string.IsNullOrEmpty(why))
            throw new DomainException("Diga por que a entrega foi refeita (ex.: o script foi recusado pela infra com o erro 229).");
        var key = ExecutionStep.NormalizeKey(definition.Key);
        if (FindStep(key) is not null)
            throw new DomainException($"Já existe a etapa '{key}' no plano — escolha outra chave.");

        var ordered = Steps.OrderBy(s => s.Order).ToList();
        var dependents = DependentsOf(redone.Key);
        var warned = dependents.Count > 0
            ? dependents
            : ordered.Skip(ordered.IndexOf(redone) + 1).Where(s => !StepSettled(s)).ToList();

        var step = new ExecutionStep(Id, key, 0, definition.Title, definition.Description, now);
        step.SetShape(definition.Executor, definition.Kind, definition.Repository, [redone.Key], definition.TaskKind);
        step.OrderPinned = true;
        ordered.Insert(ordered.IndexOf(redone) + 1, step);
        Steps.Add(step);
        step.ChangeStatus(ExecutionStatus.Running, null, actor, now);
        step.ChangeStatus(ExecutionStatus.Completed, $"Refaz '{redone.Title}': {why}", actor, now);

        var warning = $"> [!WARNING]\n> **Entrega refeita:** \"{redone.Title}\" foi refeita em \"{step.Title}\" ({why}). " +
                      "Use o resultado e os arquivos da etapa nova.";
        foreach (var s in warned)
        {
            if (s.DependsOn.Contains(redone.Key))
                s.SetShape(null, null, null, s.DependsOn.Append(step.Key));
            var text = string.IsNullOrWhiteSpace(s.Description) ? warning : $"{warning}\n\n{s.Description}";
            if (text.Length <= ExecutionStep.MaxTextLength)
                s.SetDescription(text);
            s.Touch(now);
        }

        var order = 0;
        foreach (var s in ordered)
            s.Order = order += 10;
        Touch(now, fromExecutor: false);
        return (step, warned);
    }

    /// <summary>
    /// Atualização de uma etapa pela skill. Etapa desconhecida é criada no fim (a skill nunca perde
    /// um envio por ter esquecido de declarar a etapa antes).
    /// </summary>
    public ExecutionStep UpdateStep(string key, string? status, string? reason, string? activity, string? checkpoint,
        string? title, string? description, string actor, DateTimeOffset now, string? waitingOn = null, bool fromExecutor = true)
    {
        EnsureNotCancelled();

        var normalized = ExecutionStep.NormalizeKey(key);
        var step = FindStep(normalized);
        if (step is null)
        {
            step = new ExecutionStep(Id, normalized, NextOrder(), string.IsNullOrWhiteSpace(title) ? normalized : title, description, now);
            Steps.Add(step);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(title)) step.SetTitle(title);
            if (description is not null) step.SetDescription(description);
        }

        if (activity is not null) step.SetActivity(activity);
        if (checkpoint is not null) step.SetCheckpoint(checkpoint);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLowerInvariant();
            // Card 78680: etapa pausada pela pessoa não começa (nem pelo tratamento) até alguém continuar.
            if (normalizedStatus == ExecutionStatus.Running && step.Status != ExecutionStatus.Running && HeldBlocker(step) is { } held)
                throw new DomainException(held == step
                    ? $"A etapa '{step.Key}' está pausada por {held.HeldBy ?? "uma pessoa"} — não a comece; siga para a próxima etapa pronta (prmake_plan) ou encerre a vez se não houver."
                    : $"A etapa '{step.Key}' depende de '{held.Key}', pausada por {held.HeldBy ?? "uma pessoa"} — siga para a próxima etapa pronta (prmake_plan) ou encerre a vez.");
            // 0080: base diferente da análise (branch, repositório, código que mudou) — o PR espera alguém confirmar.
            if (DivergencePending && step.Kind == ExecutionStepKind.PullRequest
                && normalizedStatus is ExecutionStatus.Running or ExecutionStatus.Completed && step.Status != normalizedStatus)
                throw new DomainException("Há uma divergência pendente entre a análise e a base real: a etapa de PR só começa depois que " +
                                          "a pessoa confirmar na tela (\"Confirmar\" no alerta de divergência). Registre e aguarde (prmake_block).");
            step.ChangeStatus(normalizedStatus, reason, actor, now, waitingOn);

            // A skill voltou a trabalhar: o plano (pendente, com falha ou já concluído) volta a
            // "em andamento". Pausado continua pausado — só o usuário (ou a retomada) tira da pausa.
            if (normalizedStatus is ExecutionStatus.Running or ExecutionStatus.Waiting
                && Status is ExecutionStatus.Pending or ExecutionStatus.Failed or ExecutionStatus.Completed)
            {
                SetPlanStatus(ExecutionStatus.Running, null, actor, now);
            }
        }
        else
        {
            step.Touch(now);
        }

        // Card 78505 (#874): só o executor conta como "Claude trabalhando" — a pessoa/o especialista mexendo na etapa não
        // pode fazer o plano parecer vivo (a fila deixaria de pedir o executor por 150 s).
        Touch(now, fromExecutor);
        return step;
    }

    /// <summary>A atividade atual da etapa acompanha o último pedaço de andamento enviado.</summary>
    public void TrackActivity(string stepKey, string message, DateTimeOffset now)
    {
        var step = FindStep(stepKey.Trim().ToLowerInvariant());
        if (step is null || ExecutionStatus.IsStepFinished(step.Status))
            return;

        var firstLine = message.Split('\n', 2)[0].Trim().TrimStart('#', '*', '-', ' ');
        if (firstLine.Length > 0)
            step.SetActivity(firstLine);
    }

    /// <summary>O usuário começa uma etapa pela tela (típico das etapas executor=user) — 0024.</summary>
    public ExecutionStep StartStepByUser(string key, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();
        var step = RequireStep(key);
        if (ExecutionStatus.IsStepFinished(step.Status))
            throw new DomainException("Esta etapa já terminou.");
        step.ChangeStatus(ExecutionStatus.Running, null, actor, now);
        if (Status is ExecutionStatus.Pending or ExecutionStatus.Failed)
            SetPlanStatus(ExecutionStatus.Running, null, actor, now);
        Touch(now, fromExecutor: false);
        return step;
    }

    /// <summary>O usuário conclui uma etapa pela tela (ex.: abriu o chamado, validou em QA) — 0024.</summary>
    public ExecutionStep CompleteStepByUser(string key, string? reason, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();
        var step = RequireStep(key);
        if (step.Status == ExecutionStatus.Completed)
            throw new DomainException("Esta etapa já foi concluída.");
        step.ChangeStatus(ExecutionStatus.Completed, string.IsNullOrWhiteSpace(reason) ? null : reason, actor, now);
        Touch(now, fromExecutor: false);
        return step;
    }

    /// <summary>Etapa passa a aguardar algo externo (resposta, chamado, merge); o plano segue — 0024.</summary>
    public ExecutionStep SetStepWaiting(string key, string reason, string waitingOn, string actor, DateTimeOffset now, bool fromExecutor)
    {
        EnsureNotCancelled();
        var step = RequireStep(key);
        if (ExecutionStatus.IsStepFinished(step.Status))
            return step;
        step.ChangeStatus(ExecutionStatus.Waiting, reason, actor, now, waitingOn);
        if (Status is ExecutionStatus.Pending or ExecutionStatus.Failed)
            SetPlanStatus(ExecutionStatus.Running, null, actor, now);
        Touch(now, fromExecutor);
        return step;
    }

    /// <summary>O que a etapa esperava chegou (ex.: todas as perguntas respondidas): volta a "em andamento".</summary>
    public ExecutionStep? ResumeStepFromWait(string key, string actor, DateTimeOffset now, bool fromExecutor)
    {
        var step = FindStep(ExecutionStep.NormalizeKey(key));
        if (step is null || step.Status != ExecutionStatus.Waiting)
            return null;
        step.ChangeStatus(ExecutionStatus.Running, null, actor, now);
        Touch(now, fromExecutor);
        return step;
    }

    /// <summary>
    /// "Já resolvi" pela tela (0037): o usuário fez o que a etapa esperava dele (liberou a permissão, ligou a VPN...).
    /// A etapa volta a "em andamento" — o vigia da skill vê a mudança e tenta de novo.
    /// </summary>
    public ExecutionStep ResolveStepByUser(string key, string? note, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();
        var step = RequireStep(key);
        if (step.Status != ExecutionStatus.Waiting || step.WaitingOn != ExecutionWaitingOn.User)
            throw new DomainException("Esta etapa não está aguardando você.");
        var trimmed = note?.Trim();
        step.ChangeStatus(ExecutionStatus.Running, string.IsNullOrEmpty(trimmed) ? $"Resolvido por {actor}" : $"Resolvido por {actor}: {trimmed}", actor, now);
        if (Status is ExecutionStatus.Pending or ExecutionStatus.Failed)
            SetPlanStatus(ExecutionStatus.Running, null, actor, now);
        Touch(now, fromExecutor: false);
        return step;
    }

    /// <summary>
    /// O que depende do usuário agora (0037): etapas do usuário prontas/em andamento e etapas travadas esperando uma
    /// ação dele. As perguntas abertas entram à parte (não são do agregado). Plano terminado/falho não tem pendência.
    /// </summary>
    public IEnumerable<ExecutionStep> StepsPendingForUser()
    {
        if (IsFinished || Status == ExecutionStatus.Failed)
            return [];
        var ready = ReadySteps().Select(s => s.Key).ToHashSet();
        return Steps.Where(s => !s.Held && (
                (s.Status == ExecutionStatus.Waiting && s.WaitingOn == ExecutionWaitingOn.User)
                || (s.Executor == ExecutionExecutor.User && (s.Status == ExecutionStatus.Running || ready.Contains(s.Key)))))
            .OrderBy(s => s.Order);
    }

    /// <summary>
    /// Recalcula a etapa pelos chamados que a bloqueiam (0024): algum aberto → aguardando; o mais recente
    /// resolvido → concluída; só fechados sem resolução → aguardando um novo chamado.
    /// </summary>
    public ExecutionStep? ApplyTicketState(string key, IReadOnlyCollection<ExecutionLink> blockingTickets, string actor, DateTimeOffset now, bool fromExecutor)
    {
        var step = FindStep(ExecutionStep.NormalizeKey(key));
        if (step is null || Status == ExecutionStatus.Cancelled || blockingTickets.Count == 0)
            return null;
        if (step.Status is ExecutionStatus.Completed or ExecutionStatus.Cancelled)
            return null;

        var open = blockingTickets.Where(t => t.Status == ExecutionLinkStatus.Open).ToList();
        if (open.Count > 0)
        {
            var label = open.Count == 1 ? $"o chamado {open[0].DisplayName}" : $"{open.Count} chamados";
            return SetStepWaiting(key, $"Aguardando {label}", ExecutionWaitingOn.External, actor, now, fromExecutor);
        }

        var latest = blockingTickets.OrderByDescending(t => t.CreatedAt).First();
        if (latest.Status == ExecutionLinkStatus.Resolved)
        {
            step.ChangeStatus(ExecutionStatus.Completed, $"Chamado {latest.DisplayName} resolvido", actor, now);
            Touch(now, fromExecutor);
            return step;
        }

        return SetStepWaiting(key, $"Chamado {latest.DisplayName} fechado sem resolução — abra outro chamado ou conclua a etapa",
            ExecutionWaitingOn.User, actor, now, fromExecutor);
    }

    /// <summary>
    /// Etapa de PR ainda pendente com PR aberto (aberto pela tela ou por uma sessão que não marcou a etapa) passa a aguardar
    /// o merge — senão fica "pronta" e o Claude tenta abrir de novo. Etapa em andamento só quando o PR acabou de ser
    /// anexado (<paramref name="includeRunning"/>); na sincronização ela fica com quem a está tocando.
    /// </summary>
    public ExecutionStep? WaitForPullRequests(string key, IReadOnlyCollection<ExecutionLink> stepLinks, string actor, DateTimeOffset now,
        bool fromExecutor, bool includeRunning = false)
    {
        var step = FindStep(ExecutionStep.NormalizeKey(key));
        if (step is null || Status == ExecutionStatus.Cancelled || step.Kind != ExecutionStepKind.PullRequest
            || !(step.Status == ExecutionStatus.Pending || (includeRunning && step.Status == ExecutionStatus.Running)))
            return null;
        var open = stepLinks.Where(l => l.Kind == ExecutionLinkKind.PullRequest && l.Status == ExecutionLinkStatus.Open).ToList();
        if (open.Count == 0)
            return null;
        var reason = open.Count == 1 ? $"Aguardando o merge do PR {open[0].DisplayName}" : $"Aguardando o merge de {open.Count} PRs";
        return SetStepWaiting(step.Key, reason, ExecutionWaitingOn.External, actor, now, fromExecutor);
    }

    /// <summary>Conclui uma etapa por uma regra automática (ex.: PRs mesclados) — 0024.</summary>
    public ExecutionStep? CompleteStep(string key, string reason, string actor, DateTimeOffset now, bool fromExecutor)
    {
        var step = FindStep(ExecutionStep.NormalizeKey(key));
        if (step is null || ExecutionStatus.IsStepFinished(step.Status) || Status == ExecutionStatus.Cancelled)
            return null;
        step.ChangeStatus(ExecutionStatus.Completed, reason, actor, now);
        Touch(now, fromExecutor);
        return step;
    }

    /// <summary>
    /// Plano de correção com todas as etapas terminadas (e ao menos uma concluída) → concluído (0024).
    /// Com etapas de PR, elas só concluem com os PRs mesclados — daí "concluído = PRs mesclados".
    /// </summary>
    public bool TryAutoComplete(string actor, DateTimeOffset now)
    {
        if (Phase != ExecutionPhase.Correction || IsFinished || Status == ExecutionStatus.Failed || Steps.Count == 0)
            return false;
        if (!Steps.All(s => s.Status is ExecutionStatus.Completed or ExecutionStatus.Cancelled) || !Steps.Any(s => s.Status == ExecutionStatus.Completed))
            return false;
        SetPlanStatus(ExecutionStatus.Completed, "Todas as etapas concluídas", actor, now);
        Touch(now, fromExecutor: false);
        return true;
    }

    /// <summary>Etapas prontas para começar: pendentes com todas as dependências terminadas.</summary>
    public IEnumerable<ExecutionStep> ReadySteps() =>
        Steps.Where(s => s.Status == ExecutionStatus.Pending && !s.Held && s.DependsOn.All(d =>
        {
            var dep = FindStep(d);
            return dep is null || dep.Status is ExecutionStatus.Completed or ExecutionStatus.Cancelled;
        })).OrderBy(s => s.Order);

    // ---------------------------------------------------------------- card 78680: ordem, pausa, dependências

    private static bool StepSettled(ExecutionStep s) => ExecutionStatus.IsStepFinished(s.Status);

    private static string StepName(ExecutionStep s) => $"A etapa '{s.Key}'";

    /// <summary>Etapas do plano de que esta depende (as chaves que não existem mais não contam).</summary>
    public IEnumerable<ExecutionStep> Dependencies(ExecutionStep step) =>
        step.DependsOn.Select(FindStep).Where(d => d is not null && d != step).Select(d => d!);

    /// <summary>Ainda não terminadas que dependem (direta ou indiretamente) da etapa, na ordem.</summary>
    public IReadOnlyList<ExecutionStep> DependentsOf(string key) =>
        TaskOrdering.Dependents(Steps.OrderBy(s => s.Order).ToList(), RequireStep(key), StepSettled, Dependencies);

    /// <summary>A etapa pausada que segura esta (ela mesma ou uma dependência ainda não terminada), ou null.</summary>
    public ExecutionStep? HeldBlocker(ExecutionStep step) =>
        step.Held ? step : TaskOrdering.Upstream(step, StepSettled, Dependencies).FirstOrDefault(d => d.Held);

    /// <summary>
    /// Muda a ordem de uma etapa ainda não terminada (início = antes da primeira não terminada; fim; acima; abaixo; antes de
    /// outra) sem quebrar dependência. A etapa em andamento que for movida, ou que ganhar outra na frente, volta a pendente
    /// (o tratamento recomeça na ordem nova — quem chama pausa e retoma o plano). Devolve as etapas interrompidas.
    /// </summary>
    public IReadOnlyList<ExecutionStep> MoveStep(string key, string position, string? beforeKey, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();
        if (IsFinished)
            throw new DomainException("O plano já terminou — a ordem das etapas não muda mais.");
        var step = RequireStep(key);
        if (StepSettled(step))
            throw new DomainException($"{StepName(step)} já terminou ({step.Status}) — a ordem só muda entre as ainda não terminadas.");
        var before = string.IsNullOrWhiteSpace(beforeKey) ? null : RequireStep(beforeKey);
        var ordered = Steps.OrderBy(s => s.Order).ToList();
        var sequence = TaskOrdering.Move(ordered, step, position, before, StepSettled, StepName);
        if (TaskOrdering.Violation(sequence, StepSettled, Dependencies) is { } v)
            throw new DomainException(v.Task == step
                ? $"{StepName(step)} depende de '{v.Dependency.Key}' ({v.Dependency.Title}) — não pode vir antes dela."
                : $"A etapa '{v.Task.Key}' ({v.Task.Title}) depende de '{v.Dependency.Key}' — '{v.Dependency.Key}' não pode ir para depois dela.");

        var interrupted = Steps.Where(s => s.Status == ExecutionStatus.Running
                                           && (s == step || sequence.IndexOf(step) < sequence.IndexOf(s) && ordered.IndexOf(step) > ordered.IndexOf(s)))
            .ToList();
        foreach (var s in interrupted)
            s.ChangeStatus(ExecutionStatus.Pending, s == step ? $"Interrompida: mudou de ordem ({actor})" : $"Interrompida: '{step.Key}' passou na frente ({actor})", actor, now);
        var order = 0;
        foreach (var s in sequence)
            s.Order = order += 10;
        step.OrderPinned = true;
        step.Touch(now);
        Touch(now, fromExecutor: false);
        return interrupted;
    }

    /// <summary>
    /// Pausa (não fica pronta, nem as que dependem dela) ou continua uma etapa ainda não terminada. Em andamento/espera, ao
    /// pausar volta a pendente (quem chama pausa e retoma o plano se o tratamento estava nela).
    /// </summary>
    public ExecutionStep HoldStep(string key, bool hold, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();
        var step = RequireStep(key);
        if (hold)
        {
            if (StepSettled(step))
                throw new DomainException($"{StepName(step)} já terminou ({step.Status}) — não há o que pausar.");
            // 0103: pausar a agendada = tirar a data (fica pausada até alguém continuar).
            if (step.Held && step.ScheduledAt is not null)
            {
                step.SetSchedule(null, null, now);
                Touch(now, fromExecutor: false);
                return step;
            }
            if (step.Status is ExecutionStatus.Running or ExecutionStatus.Waiting)
                step.ChangeStatus(ExecutionStatus.Pending, $"Pausada por {actor}", actor, now);
        }
        else if (!step.Held)
            throw new DomainException($"{StepName(step)} não está pausada.");
        step.SetHeld(hold, actor, now);
        Touch(now, fromExecutor: false);
        return step;
    }

    /// <summary>
    /// 0103: agenda a etapa — pausada até <paramref name="at"/> (no horário o PRMake continua sozinho e retoma o plano). Em
    /// andamento/espera volta a pendente (quem chama pausa e retoma o plano se o tratamento estava nela).
    /// </summary>
    public ExecutionStep ScheduleStep(string key, DateTimeOffset at, string actor, Guid? actorId, DateTimeOffset now)
    {
        EnsureNotCancelled();
        var step = RequireStep(key);
        if (StepSettled(step))
            throw new DomainException($"{StepName(step)} já terminou ({step.Status}) — não há o que agendar.");
        var when = ScheduleTime.Validate(at, now);
        if (!step.Held)
        {
            if (step.Status is ExecutionStatus.Running or ExecutionStatus.Waiting)
                step.ChangeStatus(ExecutionStatus.Pending, $"Agendada para {ScheduleTime.Format(when)} por {actor}", actor, now);
            step.SetHeld(true, actor, now);
        }
        step.SetSchedule(when, actorId, now);
        Touch(now, fromExecutor: false);
        return step;
    }

    private ExecutionStep RequireStep(string key) =>
        FindStep(ExecutionStep.NormalizeKey(key)) ?? throw new DomainException("Etapa não encontrada.");

    /// <summary>O usuário cancela (pula) uma etapa que ainda não terminou.</summary>
    public ExecutionStep CancelStep(string key, string reason, string actor, DateTimeOffset now)
    {
        EnsureNotCancelled();

        var step = FindStep(ExecutionStep.NormalizeKey(key))
                   ?? throw new DomainException("Etapa não encontrada.");
        if (ExecutionStatus.IsStepFinished(step.Status))
            throw new DomainException("Esta etapa já terminou e não pode ser cancelada.");

        step.ChangeStatus(ExecutionStatus.Cancelled, reason, actor, now);
        step.SetHeld(false, null, now);
        Touch(now, fromExecutor: false);
        return step;
    }

    /// <summary>
    /// Troca o status do plano (usuário: pausar/continuar/cancelar; skill: retomar/concluir/falhar).
    /// </summary>
    public void ChangeStatus(string status, string? reason, string? summary, string actor, bool fromExecutor, DateTimeOffset now)
    {
        var target = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (!ExecutionStatus.PlanStatuses.Contains(target) || target == ExecutionStatus.Pending)
            throw new DomainException($"Status de plano inválido: '{status}'.");

        if (Status == ExecutionStatus.Cancelled)
            throw new DomainException("O plano foi cancelado e não pode mudar de status.");

        switch (target)
        {
            case ExecutionStatus.Paused:
                if (Status is not (ExecutionStatus.Pending or ExecutionStatus.Running))
                    throw new DomainException("Só um plano pendente ou em andamento pode ser pausado.");
                break;

            case ExecutionStatus.Running:
                // Continuar (usuário) ou retomar (skill). Concluído só volta pela skill (reabrir).
                if (Status == ExecutionStatus.Completed && !fromExecutor)
                    throw new DomainException("O plano já foi concluído.");
                break;

            case ExecutionStatus.Cancelled:
                if (Status == ExecutionStatus.Completed)
                    throw new DomainException("O plano já foi concluído.");
                var cancelReason = string.IsNullOrWhiteSpace(reason) ? $"Plano cancelado por {actor}" : reason.Trim();
                foreach (var step in Steps.Where(s => !ExecutionStatus.IsStepFinished(s.Status)))
                    step.ChangeStatus(ExecutionStatus.Cancelled, cancelReason, actor, now);
                break;

            case ExecutionStatus.Completed:
                foreach (var step in Steps.Where(s => s.Status == ExecutionStatus.Running))
                    step.ChangeStatus(ExecutionStatus.Completed, null, actor, now);
                foreach (var step in Steps.Where(s => s.Status is ExecutionStatus.Pending or ExecutionStatus.Waiting))
                    step.ChangeStatus(ExecutionStatus.Cancelled, "Não executada", actor, now);
                break;

            case ExecutionStatus.Failed:
                foreach (var step in Steps.Where(s => s.Status is ExecutionStatus.Running or ExecutionStatus.Waiting))
                    step.ChangeStatus(ExecutionStatus.Failed, reason, actor, now);
                break;
        }

        SetSummary(summary);
        SetPlanStatus(target, reason, actor, now);
        Touch(now, fromExecutor);
    }

    private void SetPlanStatus(string status, string? reason, string actor, DateTimeOffset now)
    {
        if (status == ExecutionStatus.Running)
        {
            StartedAt ??= now;
            FinishedAt = null;
        }
        else if (status is ExecutionStatus.Completed or ExecutionStatus.Cancelled or ExecutionStatus.Failed)
        {
            FinishedAt = now;
        }

        Status = status;
        var trimmed = reason?.Trim();
        StatusReason = string.IsNullOrEmpty(trimmed)
            ? null
            : trimmed.Length <= ExecutionStep.MaxReasonLength ? trimmed : trimmed[..ExecutionStep.MaxReasonLength];
        StatusChangedBy = actor;
    }

    private void EnsureNotCancelled()
    {
        if (Status == ExecutionStatus.Cancelled)
            throw new DomainException("O plano foi cancelado. Crie um novo plano para continuar.");
    }

    private ExecutionStep? FindStep(string key) => Steps.FirstOrDefault(s => s.Key == key);

    private int NextOrder() => (Steps.Count == 0 ? 0 : Steps.Max(s => s.Order)) + 10;
}
