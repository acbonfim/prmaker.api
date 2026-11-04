using System.Globalization;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// 0077: assistente do card — o PRMake conversa com quem trata o card sem travar nada: pergunta se registra a estimativa
/// inicial, se publica sozinho os resumos de espera na discussion e sugere lançar horas em Completed conforme as etapas
/// avançam. Cada sugestão só aparece uma vez por card; quem responde é a pessoa (a ação usa a integração dela).
/// </summary>
public partial class ExecutionPlanApplication
{
    public async Task<List<ExecutionSuggestionResponse>> GetSuggestionsAsync(string cardNumber, CancellationToken cancellationToken) =>
        (await _repository.GetSuggestionsAsync(cardNumber.Trim(), cancellationToken))
            .OrderBy(s => s.IsOpen ? 0 : 1).ThenByDescending(s => s.CreatedAt)
            .Select(ExecutionSuggestionResponse.From).ToList();

    public async Task<ExecutionSuggestionResponse> ActOnSuggestionAsync(Guid suggestionId, string action, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var suggestion = await _repository.FindSuggestionAsync(suggestionId, cancellationToken)
                         ?? throw new ExecutionPlanNotFoundException("Sugestão não encontrada.");
        suggestion.Reopen();
        if (!suggestion.IsOpen)
            throw new DomainException("Esta sugestão já foi respondida.");
        var key = (action ?? string.Empty).Trim();
        if (suggestion.Actions.All(a => a.Key != key))
            throw new DomainException($"Ação inválida: '{action}'.");

        var (ok, result) = await RunSuggestionAsync(suggestion, key, actor, cancellationToken);
        suggestion.Resolve(key, ok, result, actor.Name, DateTimeOffset.UtcNow);
        await _repository.SaveChangesAsync(cancellationToken);
        await NotifySuggestionAsync(suggestion, cancellationToken);
        return ExecutionSuggestionResponse.From(suggestion);
    }

    public async Task<ExecutionSuggestionResponse> DismissSuggestionAsync(Guid suggestionId, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var suggestion = await _repository.FindSuggestionAsync(suggestionId, cancellationToken)
                         ?? throw new ExecutionPlanNotFoundException("Sugestão não encontrada.");
        suggestion.Dismiss(actor.Name, DateTimeOffset.UtcNow);
        await _repository.SaveChangesAsync(cancellationToken);
        await NotifySuggestionAsync(suggestion, cancellationToken);
        return ExecutionSuggestionResponse.From(suggestion);
    }

    /// <summary>Publicar sozinho a situação só neste card (null = volta para a preferência do dono / padrão).</summary>
    public async Task<CardSituationResponse> SetCardSituationAutoPostAsync(string cardNumber, bool? autoPost, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var card = cardNumber.Trim();
        var planId = await _repository.GetCurrentPlanIdAsync(card, cancellationToken)
                     ?? throw new ExecutionPlanNotFoundException("O card não tem plano.");
        var plan = await MutateAsync(planId, p => p.SetSituationAutoPost(autoPost, DateTimeOffset.UtcNow), cancellationToken, actor);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Situation, null, cancellationToken);
        if (autoPost == true)
            await PostSituationToDevOpsAsync(planId, card, cancellationToken);
        return await GetSituationAsync(card, false, cancellationToken);
    }

    /// <summary>Textos gerados (análise, soluções, chamado, orientação...) editados pela tela — só arquivos de texto da skill.</summary>
    public async Task<ExecutionArtifactResponse> UpdateArtifactTextAsync(Guid planId, Guid artifactId, string content, ExecutionActor actor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new DomainException("O texto não pode ficar vazio.");
        ExecutionArtifact? saved = null;
        var plan = await MutateAsync(planId, async p =>
        {
            var artifact = await _repository.FindArtifactWithContentAsync(p.Id, artifactId, cancellationToken)
                           ?? throw new ExecutionPlanNotFoundException("Arquivo não encontrado.");
            var isText = artifact.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                         || artifact.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
            if (!isText || artifact.NoteId is not null || artifact.IsEvidence)
                throw new DomainException("Só os textos gerados no plano podem ser editados aqui (não anexos nem evidências do DevOps).");
            var now = DateTimeOffset.UtcNow;
            artifact.SetContent(System.Text.Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n")), artifact.ContentType, null,
                $"Editado por {actor.Name} em {TimeZoneInfo.ConvertTime(now, CardSituation.Brasilia):dd/MM/yyyy HH:mm}", now, false);
            p.Touch(now, fromExecutor: actor.IsExecutor);
            saved = artifact;
        }, cancellationToken, actor);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Artifact, saved!.StepKey, cancellationToken);
        return saved.ToResponse();
    }

    private async Task<(bool Ok, string? Result)> RunSuggestionAsync(ExecutionSuggestion s, string action, ExecutionActor actor, CancellationToken cancellationToken)
    {
        if (action == "skip")
            return (true, "Agora não");
        if (_devOps is null)
            return (false, "Integração com o DevOps indisponível.");
        switch (s.Kind)
        {
            case ExecutionSuggestionKind.InitialEstimate:
                return await _devOps.SetInitialEstimateAsync(s.CardNumber, cancellationToken);
            case ExecutionSuggestionKind.LogHours:
                var hours = decimal.Parse(action.TrimEnd('h'), CultureInfo.InvariantCulture);
                return await _devOps.LogHoursAsync(s.CardNumber, hours, cancellationToken);
            case ExecutionSuggestionKind.SituationAutoPost:
                if (action == "always" && actor.UserId is { } userId)
                    await _repository.SetSituationAutoPostAsync(userId, true, cancellationToken);
                await SetCardSituationAutoPostAsync(s.CardNumber, action != "no", actor, cancellationToken);
                return (true, action switch
                {
                    "always" => "Publico sozinho em todos os seus cards (dá para mudar no cartão da situação).",
                    "card" => "Publico sozinho neste card.",
                    _ => "Você publica pelo cartão da situação quando quiser."
                });
            default:
                return (false, "Sugestão desconhecida.");
        }
    }

    /// <summary>Plano de análise criado: estimativa inicial (card sem estimativa) e publicação dos resumos de espera.</summary>
    private async Task SuggestOnCreateAsync(ExecutionPlan plan, CancellationToken cancellationToken)
    {
        if (_devOps is null || plan.Phase != ExecutionPhase.Analysis) return;
        try
        {
            var settings = await _devOps.GetAssistantSettingsAsync(cancellationToken);
            if (!settings.Enabled) return;
            var now = DateTimeOffset.UtcNow;
            var added = false;

            var effort = await _devOps.GetEffortAsync(plan.CardNumber, cancellationToken);
            if (effort is { OriginalEstimate: null or 0m })
                added |= await AddSuggestionAsync(new ExecutionSuggestion(plan.Id, plan.CardNumber, ExecutionSuggestionKind.InitialEstimate, "initial-estimate",
                    "Registrar a estimativa inicial?",
                    "O card ainda não tem estimativa no DevOps. Registro a estimativa inicial configurada (Original Estimate e Remaining Work)" +
                    $" — a média do time é {Hours(settings.AverageHours)} por card.",
                    [new() { Key = "apply", Label = "Registrar estimativa", Primary = true }, new() { Key = "skip", Label = "Agora não" }], now), cancellationToken);

            var own = plan.CreatedByUserId is { } owner ? await _repository.GetSituationAutoPostAsync(owner, cancellationToken) : null;
            if (own is null)
                added |= await AddSuggestionAsync(new ExecutionSuggestion(plan.Id, plan.CardNumber, ExecutionSuggestionKind.SituationAutoPost, "situation-auto-post",
                    "Publicar os resumos de espera na discussion?",
                    "Quando o card ficar esperando algo — chamado, replicar no ambiente, aprovação do PR, validação em QA — posso publicar " +
                    "um resumo na discussion do DevOps e mantê-lo atualizado (sempre o mesmo comentário).",
                    [new() { Key = "card", Label = "Sim, neste card", Primary = true }, new() { Key = "always", Label = "Sim, sempre" },
                     new() { Key = "no", Label = "Não, eu publico" }], now), cancellationToken);

            if (added)
                await NotifySuggestionAsync(plan, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // O assistente nunca atrapalha o plano.
        }
    }

    /// <summary>Etapas que acabaram de concluir: sugere lançar horas (uma vez por etapa, pela config).</summary>
    private async Task SuggestAfterStepsAsync(PlanSnapshot before, ExecutionPlan plan, CancellationToken cancellationToken)
    {
        if (_devOps is null) return;
        var done = plan.Steps
            .Where(s => s.Status == ExecutionStatus.Completed && before.Steps.TryGetValue(s.Key, out var old) && old.Status != ExecutionStatus.Completed)
            .ToList();
        if (done.Count == 0) return;
        try
        {
            var settings = await _devOps.GetAssistantSettingsAsync(cancellationToken);
            if (!settings.Enabled) return;
            var step = done.LastOrDefault(s => settings.SuggestsHoursFor(s.Key));
            if (step is null || await _repository.SuggestionExistsAsync(plan.CardNumber, $"log-hours:{step.Key}", cancellationToken)) return;

            var effort = await _devOps.GetEffortAsync(plan.CardNumber, cancellationToken);
            var h = settings.HoursPerStep;
            var text = $"*{step.Title}* concluída.";
            if (effort is not null)
            {
                var completed = effort.CompletedWork ?? 0m;
                var remaining = effort.RemainingWork ?? 0m;
                text += $" Hoje o card tem {Hours(completed)} em Completed e {Hours(remaining)} em Remaining; lançando {Hours(h)} fica " +
                        $"{Hours(completed + h)} e {Hours(Math.Max(0m, remaining - h))}.";
                if (completed + h > settings.AverageHours)
                    text += $" Passa da média do time ({Hours(settings.AverageHours)} por card) — vale conferir se a complexidade justifica.";
            }
            if (await AddSuggestionAsync(new ExecutionSuggestion(plan.Id, plan.CardNumber, ExecutionSuggestionKind.LogHours, $"log-hours:{step.Key}",
                    $"Lançar {Hours(h)} em Completed?", text,
                    [new() { Key = $"{h.ToString(CultureInfo.InvariantCulture)}h", Label = $"Lançar {Hours(h)}", Primary = true },
                     new() { Key = $"{(h * 2).ToString(CultureInfo.InvariantCulture)}h", Label = $"Lançar {Hours(h * 2)}" },
                     new() { Key = "skip", Label = "Agora não" }], DateTimeOffset.UtcNow), cancellationToken))
                await NotifySuggestionAsync(plan, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // O assistente nunca atrapalha o plano.
        }
    }

    private async Task<bool> AddSuggestionAsync(ExecutionSuggestion suggestion, CancellationToken cancellationToken)
    {
        if (await _repository.SuggestionExistsAsync(suggestion.CardNumber, suggestion.DedupKey, cancellationToken))
            return false;
        _repository.AddSuggestion(suggestion);
        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Outra requisição criou a mesma sugestão no mesmo instante (índice único por card + chave).
            _repository.ClearTracking();
            return false;
        }
    }

    private Task NotifySuggestionAsync(ExecutionSuggestion s, CancellationToken cancellationToken) =>
        NotifySuggestionAsync(s.CardNumber, s.PlanId, cancellationToken);

    private Task NotifySuggestionAsync(ExecutionPlan plan, CancellationToken cancellationToken) =>
        NotifySuggestionAsync(plan.CardNumber, plan.Id, cancellationToken);

    private async Task NotifySuggestionAsync(string card, Guid planId, CancellationToken cancellationToken)
    {
        try
        {
            await _realTimeNotifier.NotifyGroupAsync(ExecutionPlanRealTimeEvents.Group(card), ExecutionPlanRealTimeEvents.EventPlanUpdated,
                new { cardNumber = card, planId, action = ExecutionPlanRealTimeEvents.Actions.Suggestion, stepKey = (string?)null }, cancellationToken);
        }
        catch
        {
            // Tempo real é best-effort.
        }
    }

    private static string Hours(decimal h) => $"{h.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"))}h";
}
