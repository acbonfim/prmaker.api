using System.IO.Compression;
using Cime.BuildingBlocks.RealTime;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

public class ExecutionPlanApplication : IExecutionPlanApplication
{
    /// <summary>Máximo de pedaços por lote (a fila local da skill reenvia em lotes).</summary>
    public const int MaxLogsPerRequest = 200;

    private const int MaxLogsPerPage = 1000;
    private const int ConcurrencyRetries = 8;

    private readonly IExecutionPlanRepository _repository;
    private readonly IRealTimeNotifier _realTimeNotifier;

    public ExecutionPlanApplication(IExecutionPlanRepository repository, IRealTimeNotifier realTimeNotifier)
    {
        _repository = repository;
        _realTimeNotifier = realTimeNotifier;
    }

    public async Task<ExecutionPlanResponse> CreateAsync(CreateExecutionPlanRequest request, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new ExecutionPlan(request.CardNumber, request.Kind, request.Title, request.Summary, actor.UserId, actor.Name, now);
        if (request.Steps.Count > 0)
            plan.UpsertSteps(request.Steps, now);

        _repository.AddPlan(plan);
        await _repository.SaveChangesAsync(cancellationToken);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Created, null, cancellationToken);
        return plan.ToResponse([], 0, now);
    }

    public Task<List<ExecutionPlanSummaryResponse>> GetByCardAsync(string cardNumber, CancellationToken cancellationToken) =>
        _repository.GetSummariesByCardAsync(cardNumber.Trim(), cancellationToken);

    public async Task<ExecutionPlanResponse?> GetCurrentAsync(string cardNumber, CancellationToken cancellationToken)
    {
        var id = await _repository.GetCurrentPlanIdAsync(cardNumber.Trim(), cancellationToken);
        return id is null ? null : await GetAsync(id.Value, cancellationToken);
    }

    public async Task<ExecutionPlanResponse> GetAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await LoadAsync(planId, cancellationToken);
        return await BuildResponseAsync(plan, cancellationToken);
    }

    public async Task<ExecutionPlanResponse> UpsertStepsAsync(Guid planId, UpsertExecutionStepsRequest request, ExecutionActor actor, CancellationToken cancellationToken)
    {
        if (request.Steps.Count == 0)
            throw new DomainException("Informe ao menos uma etapa.");

        var plan = await MutateAsync(planId, p => p.UpsertSteps(request.Steps, DateTimeOffset.UtcNow), cancellationToken);
        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Steps, null, cancellationToken);
        return await BuildResponseAsync(plan, cancellationToken);
    }

    public async Task<ExecutionStepResponse> UpdateStepAsync(Guid planId, string stepKey, UpdateExecutionStepRequest request, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionStep? step = null;
        var plan = await MutateAsync(planId, p =>
        {
            step = p.UpdateStep(stepKey, request.Status, request.Reason, request.Activity, request.Checkpoint,
                request.Title, request.Description, actor.Name, DateTimeOffset.UtcNow);
        }, cancellationToken);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Step, step!.Key, cancellationToken);
        return step.ToResponse();
    }

    public async Task<ExecutionStepResponse> CancelStepAsync(Guid planId, string stepKey, string reason, ExecutionActor actor, CancellationToken cancellationToken)
    {
        ExecutionStep? step = null;
        var plan = await MutateAsync(planId, p =>
        {
            var why = string.IsNullOrWhiteSpace(reason) ? $"Cancelada por {actor.Name}" : $"{reason.Trim()} — {actor.Name}";
            step = p.CancelStep(stepKey, why, actor.Name, DateTimeOffset.UtcNow);
        }, cancellationToken);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Step, step!.Key, cancellationToken);
        return step.ToResponse();
    }

    public async Task<ExecutionPlanResponse> ChangeStatusAsync(Guid planId, ChangeExecutionPlanStatusRequest request, ExecutionActor actor, CancellationToken cancellationToken)
    {
        var plan = await MutateAsync(planId, p =>
            p.ChangeStatus(request.Status, request.Reason, request.Summary, actor.Name, actor.IsExecutor, DateTimeOffset.UtcNow),
            cancellationToken);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Status, null, cancellationToken);
        return await BuildResponseAsync(plan, cancellationToken);
    }

    public async Task<ExecutionControlResponse> ControlAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await MutateAsync(planId, p => p.Touch(DateTimeOffset.UtcNow, fromExecutor: true), cancellationToken);

        var action = plan.Status switch
        {
            ExecutionStatus.Paused => "wait",
            ExecutionStatus.Cancelled or ExecutionStatus.Completed => "stop",
            _ => "continue"
        };

        // Sem evento de tempo real: o heartbeat é frequente e a tela calcula "sem sinal" sozinha.
        return new ExecutionControlResponse
        {
            PlanId = plan.Id,
            Status = plan.Status,
            StatusReason = plan.StatusReason,
            StatusChangedBy = plan.StatusChangedBy,
            Action = action,
            CancelledSteps = plan.Steps
                .Where(s => s.Status == ExecutionStatus.Cancelled)
                .OrderBy(s => s.Order)
                .Select(s => s.Key)
                .ToList()
        };
    }

    public async Task<int> AppendLogsAsync(Guid planId, AppendExecutionLogsRequest request, CancellationToken cancellationToken)
    {
        if (request.Logs.Count == 0)
            return 0;
        if (request.Logs.Count > MaxLogsPerRequest)
            throw new DomainException($"Envie no máximo {MaxLogsPerRequest} registros por vez.");

        var appended = 0;
        string? lastStepKey = null;
        var plan = await MutateAsync(planId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            var logs = request.Logs.Select(l => new ExecutionLog(p.Id, l.StepKey, l.Kind, l.Message, l.ClientId, now)).ToList();

            // Reenvio da fila local da skill: o mesmo clientId não entra duas vezes (nem no mesmo lote).
            var clientIds = logs.Where(l => l.ClientId is not null).Select(l => l.ClientId!).Distinct().ToList();
            var existing = clientIds.Count == 0
                ? []
                : await _repository.GetExistingClientIdsAsync(p.Id, clientIds, cancellationToken);
            var fresh = logs.Where(l => l.ClientId is null || existing.Add(l.ClientId)).ToList();

            foreach (var log in fresh.Where(l => l.StepKey is not null && l.Kind is ExecutionLogKind.Info or ExecutionLogKind.Progress))
                p.TrackActivity(log.StepKey!, log.Message, now);

            _repository.AddLogs(fresh);
            p.Touch(now, fromExecutor: true);
            appended = fresh.Count;
            lastStepKey = fresh.LastOrDefault(l => l.StepKey is not null)?.StepKey;
        }, cancellationToken);

        if (appended > 0)
            await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Log, lastStepKey, cancellationToken);
        return appended;
    }

    public async Task<List<ExecutionLogResponse>> GetLogsAsync(Guid planId, long afterId, string? stepKey, int limit, CancellationToken cancellationToken)
    {
        await LoadAsync(planId, cancellationToken);
        var key = string.IsNullOrWhiteSpace(stepKey) ? null : ExecutionStep.NormalizeKey(stepKey);
        var logs = await _repository.GetLogsAsync(planId, Math.Max(0, afterId), key, Math.Clamp(limit, 1, MaxLogsPerPage), cancellationToken);
        return logs.Select(l => l.ToResponse()).ToList();
    }

    public async Task<ExecutionArtifactResponse> UploadArtifactAsync(Guid planId, ExecutionArtifactUpload upload, ExecutionActor actor, CancellationToken cancellationToken)
    {
        if (upload.Data.LongLength == 0)
            throw new DomainException("O arquivo está vazio.");
        if (upload.Data.LongLength > ExecutionArtifact.MaxFileBytes)
            throw new DomainException($"O arquivo passa do limite de {ExecutionArtifact.MaxFileBytes / (1024 * 1024)} MB.");

        var name = ExecutionArtifact.NormalizeName(upload.FileName);
        var kind = ExecutionArtifact.NormalizeKind(upload.Kind, name);
        ExecutionArtifact? saved = null;

        var plan = await MutateAsync(planId, async p =>
        {
            var now = DateTimeOffset.UtcNow;
            var artifact = await _repository.FindArtifactAsync(p.Id, kind, name, cancellationToken);
            var isNew = artifact is null;

            var otherFiles = await _repository.GetArtifactsSizeAsync(p.Id, artifact?.Id, cancellationToken);
            if (otherFiles + upload.Data.LongLength > ExecutionArtifact.MaxPlanBytes)
                throw new DomainException($"Os arquivos deste plano passariam do limite de {ExecutionArtifact.MaxPlanBytes / (1024 * 1024)} MB.");

            artifact ??= new ExecutionArtifact(p.Id, name, kind, actor.Name, now);
            artifact.SetContent(upload.Data, upload.ContentType, upload.StepKey, upload.Description, now, isNew);
            if (isNew)
                _repository.AddArtifact(artifact);

            p.Touch(now, fromExecutor: actor.IsExecutor);
            saved = artifact;
        }, cancellationToken);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Artifact, saved!.StepKey, cancellationToken);
        return saved.ToResponse();
    }

    public async Task<ExecutionArtifactFile> GetArtifactFileAsync(Guid planId, Guid artifactId, CancellationToken cancellationToken)
    {
        var artifact = await _repository.GetArtifactAsync(planId, artifactId, cancellationToken)
                       ?? throw new ExecutionPlanNotFoundException("Arquivo não encontrado.");
        var data = await _repository.GetArtifactContentAsync(artifact.Id, cancellationToken) ?? [];
        return new ExecutionArtifactFile(artifact.ToResponse(), data);
    }

    public async Task DeleteArtifactAsync(Guid planId, Guid artifactId, CancellationToken cancellationToken)
    {
        var plan = await MutateAsync(planId, async p =>
        {
            _ = await _repository.GetArtifactAsync(p.Id, artifactId, cancellationToken)
                ?? throw new ExecutionPlanNotFoundException("Arquivo não encontrado.");
            await _repository.RemoveArtifactAsync(artifactId, cancellationToken);
            p.Touch(DateTimeOffset.UtcNow, fromExecutor: false);
        }, cancellationToken);

        await NotifyAsync(plan, ExecutionPlanRealTimeEvents.Actions.Artifact, null, cancellationToken);
    }

    public async Task<(string FileName, byte[] Data)> BuildZipAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await LoadAsync(planId, cancellationToken);
        var artifacts = await _repository.GetArtifactsAsync(planId, cancellationToken);
        if (artifacts.Count == 0)
            throw new DomainException("O plano ainda não tem arquivos.");

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var artifact in artifacts.OrderBy(a => a.Kind).ThenBy(a => a.Name))
            {
                var data = await _repository.GetArtifactContentAsync(artifact.Id, cancellationToken) ?? [];
                var entry = zip.CreateEntry($"{FolderOf(artifact.Kind)}/{artifact.Name}", CompressionLevel.Optimal);
                entry.LastWriteTime = artifact.UpdatedAt ?? artifact.CreatedAt;
                await using var stream = await entry.OpenAsync(cancellationToken);
                await stream.WriteAsync(data, cancellationToken);
            }
        }

        return ($"card-{plan.CardNumber}-{plan.Kind}-{plan.CreatedAt:yyyyMMdd-HHmm}.zip", buffer.ToArray());
    }

    private static string FolderOf(string kind) => kind switch
    {
        ExecutionArtifactKind.Script => "scripts",
        ExecutionArtifactKind.Analysis => "analises",
        ExecutionArtifactKind.Data => "dados",
        ExecutionArtifactKind.Image => "imagens",
        _ => "anexos"
    };

    private async Task<ExecutionPlan> LoadAsync(Guid planId, CancellationToken cancellationToken) =>
        await _repository.GetPlanWithStepsAsync(planId, cancellationToken)
        ?? throw new ExecutionPlanNotFoundException("Plano de execução não encontrado.");

    private Task<ExecutionPlan> MutateAsync(Guid planId, Action<ExecutionPlan> mutate, CancellationToken cancellationToken) =>
        MutateAsync(planId, p => { mutate(p); return Task.CompletedTask; }, cancellationToken);

    /// <summary>
    /// Carrega, altera e salva o plano. Se outra requisição gravou no meio (a skill mandando andamento
    /// enquanto o usuário pausa, por exemplo), relê e reaplica — ninguém perde a própria alteração.
    /// </summary>
    private async Task<ExecutionPlan> MutateAsync(Guid planId, Func<ExecutionPlan, Task> mutate, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var plan = await LoadAsync(planId, cancellationToken);
            await mutate(plan);
            try
            {
                await _repository.SaveChangesAsync(cancellationToken);
                return plan;
            }
            catch (ExecutionPlanConcurrencyException) when (attempt < ConcurrencyRetries)
            {
                _repository.ClearTracking();
                // Espera curta e aleatória: rajadas simultâneas não voltam a colidir no mesmo instante.
                await Task.Delay(Random.Shared.Next(10, 40) * attempt, cancellationToken);
            }
        }
    }

    private async Task<ExecutionPlanResponse> BuildResponseAsync(ExecutionPlan plan, CancellationToken cancellationToken)
    {
        var artifacts = await _repository.GetArtifactsAsync(plan.Id, cancellationToken);
        var lastLogId = await _repository.GetLastLogIdAsync(plan.Id, cancellationToken);
        return plan.ToResponse(artifacts, lastLogId, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Avisa quem está no card (sinal + refetch, como a timeline). Best-effort: nunca quebra a operação.
    /// </summary>
    private async Task NotifyAsync(ExecutionPlan plan, string action, string? stepKey, CancellationToken cancellationToken)
    {
        try
        {
            await _realTimeNotifier.NotifyGroupAsync(
                ExecutionPlanRealTimeEvents.Group(plan.CardNumber),
                ExecutionPlanRealTimeEvents.EventPlanUpdated,
                new { cardNumber = plan.CardNumber, planId = plan.Id, action, stepKey, status = plan.Status },
                cancellationToken);
        }
        catch
        {
            // Tempo real é best-effort; a tela também consulta periodicamente enquanto o plano está ativo.
        }
    }
}
