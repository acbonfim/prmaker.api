using Cime.BuildingBlocks.RealTime;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// 0085 r5 (1.2): arquivos do item de trabalho. A lista junta os do item com os do plano ligado (marcados
/// <c>source:"plan"</c>, sub-tarefa pela etapa); gravar no item segue as regras dos arquivos do plano: limite por arquivo e
/// por item, mesmo tipo + nome substitui e, no item da análise, as recusas de <see cref="ExecutionPhaseFiles.Reject"/>.
/// </summary>
public class WorkItemFileApplication(IWorkItemFileRepository files, IRealTimeNotifier realTime) : IWorkItemFileApplication
{
    private const int Retries = 5;

    public async Task<IReadOnlyList<WorkItemFileResponse>> ListAsync(long number, CancellationToken ct)
    {
        var item = await files.GetItemAsync(number, ct) ?? throw NotFound(number);
        var own = await files.ListAsync(item.Id, ct);
        var plan = item.PlanId is { } planId ? await files.GetPlanArtifactsAsync(planId, ct) : [];
        return WorkItemFileListing.Build(item.Number, own, plan, TaskByStep(item));
    }

    public async Task<IReadOnlyList<WorkItemFileResponse>> ListByCardAsync(string cardNumber, CancellationToken ct)
    {
        var card = cardNumber?.Trim() ?? string.Empty;
        var items = await files.GetItemsByCardAsync(card, ct);
        var own = await files.ListByItemsAsync(items.Select(i => i.Id).ToList(), ct);
        var plan = await files.GetCardPlanArtifactsAsync(card, ct);
        return WorkItemFileListing.BuildForCard(items, own, plan);
    }

    public async Task<WorkItemFileData> GetContentAsync(long number, Guid fileId, CancellationToken ct)
    {
        var item = await files.GetItemAsync(number, ct) ?? throw NotFound(number);
        var file = await files.GetAsync(item.Id, fileId, ct)
                   ?? throw new ExecutionPlanNotFoundException($"Arquivo não encontrado no item #{number}.");
        var data = await files.GetContentAsync(file.Id, ct) ?? throw new ExecutionPlanNotFoundException("Arquivo sem conteúdo.");
        return new WorkItemFileData(WorkItemFileResponse.From(file, item.Number), data);
    }

    public async Task<string> CardOfAsync(long number, CancellationToken ct) =>
        (await files.GetItemAsync(number, ct) ?? throw NotFound(number)).CardNumber;

    public async Task<WorkItemFileResponse> UploadAsync(long number, WorkItemFileUpload upload, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var item = await files.GetItemAsync(number, ct) ?? throw NotFound(number);
        var data = upload.Data ?? [];
        if (data.LongLength == 0)
            throw new DomainException("O arquivo está vazio.");
        if (data.LongLength > WorkItemFile.MaxFileBytes)
            throw new DomainException($"O arquivo passa do limite de {WorkItemFile.MaxFileBytes / (1024 * 1024)} MB.");

        var name = ExecutionArtifact.NormalizeName(upload.FileName);
        var kind = WorkItemFileKind.Normalize(upload.Kind, name);

        long? taskNumber = upload.TaskNumber is > 0 ? upload.TaskNumber : null;
        if (taskNumber is { } tn && item.Tasks.All(t => t.Number != tn))
            throw new DomainException($"A sub-tarefa #{tn} não é do item #{item.Number}.");

        // Item da análise: as mesmas recusas dos arquivos do plano de análise (script que altera dados, chamado).
        if (await PhaseOfAsync(item, ct) == ExecutionPhase.Analysis)
        {
            var phaseKind = kind == WorkItemFileKind.Text ? ExecutionArtifactKind.Analysis : kind;
            if (ExecutionPhaseFiles.Reject(ExecutionPhase.Analysis, phaseKind, name, data) is { } reason)
                throw new DomainException(reason);
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                files.ClearTracking();
                var now = DateTimeOffset.UtcNow;
                var existing = await files.FindForUpdateAsync(item.Id, kind, name, ct);
                var total = await files.GetSizeAsync(item.Id, existing?.Id, ct);
                if (total + data.LongLength > WorkItemFile.MaxItemBytes)
                    throw new DomainException($"Os arquivos do item passariam do limite de {WorkItemFile.MaxItemBytes / (1024 * 1024)} MB.");

                var file = existing ?? new WorkItemFile(item.Id, await files.GetMaxNumberAsync(item.Id, ct) + 1, name, kind, actor.Name, now);
                file.SetContent(data, upload.ContentType, taskNumber, now, isNew: existing is null);
                if (existing is null)
                    files.Add(file);
                files.AddLog(new WorkItemLog(item.Id, file.TaskNumber, actor.Name, WorkItemLogKind.Info,
                    $"Arquivo #{file.Number} {file.Name} ({file.Kind}) {(existing is null ? "gravado" : "atualizado")}.", now));
                await files.SaveChangesAsync(ct);

                await TryAsync(() => realTime.NotifyGroupAsync(
                    ExecutionPlanRealTimeEvents.Group(item.CardNumber),
                    ExecutionPlanRealTimeEvents.EventWorkItemsUpdated,
                    new
                    {
                        cardNumber = item.CardNumber, number = item.Number, taskNumber = file.TaskNumber,
                        action = ExecutionPlanRealTimeEvents.WorkItemActions.File, status = item.Status, fileId = file.Id,
                    },
                    ct));
                return WorkItemFileResponse.From(file, item.Number);
            }
            catch (WorkItemFileNumberConflictException) when (attempt < Retries)
            {
                await Task.Delay(Random.Shared.Next(10, 50) * attempt, ct);
            }
        }
    }

    /// <summary>Etapa do plano → sub-tarefa do item (mesmo StepKey).</summary>
    public static IReadOnlyDictionary<string, long> TaskByStep(WorkItem item) => WorkItemFileListing.TaskByStep(item);

    private async Task<string?> PhaseOfAsync(WorkItem item, CancellationToken ct)
    {
        if (item.PlanId is { } planId)
            return await files.GetPlanPhaseAsync(planId, ct);
        return item.Origin == WorkItemOrigin.Analysis ? ExecutionPhase.Analysis : null;
    }

    private static ExecutionPlanNotFoundException NotFound(long number) => new($"Item de trabalho #{number} não encontrado.");

    /// <summary>Tempo real é best-effort: nunca quebra a gravação.</summary>
    private static async Task TryAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is not OperationCanceledException) { }
    }
}
