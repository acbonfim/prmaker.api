using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cime.BuildingBlocks.RealTime;
using Microsoft.Extensions.Logging;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// 0091: entregas do item de trabalho — o que cada sub-tarefa gerou de fato. A lista junta as gravadas com as deduzidas
/// do que já existia (<see cref="WorkDeliverableListing"/>); o diff do código fica gravado (fotografia), o de PR/branch sai
/// do compare do GitHub.
/// </summary>
public class WorkItemDeliverableApplication(IWorkItemDeliverableRepository repo, IRealTimeNotifier realTime,
    ILogger<WorkItemDeliverableApplication> logger, IWorkDeliverableSources? sources = null) : IWorkItemDeliverableApplication
{
    private const int Retries = 5;
    private const int MaxFileList = 300;

    public async Task<List<WorkDeliverableResponse>> ListAsync(long number, CancellationToken ct) =>
        await ListAsync(await repo.GetItemAsync(number, ct) ?? throw NotFound(number), ct);

    public async Task<List<WorkDeliverableResponse>> ListAsync(WorkItem item, CancellationToken ct)
    {
        var stored = await repo.ListAsync(item.Id, ct);
        var links = item.PlanId is { } planId ? await repo.GetPlanLinksAsync(planId, ct) : [];
        IReadOnlyList<DeliverableCardPr> prs = [];
        if (sources is not null && (item.Tasks.Any(t => t.Kind == WorkTaskKind.PullRequest) || stored.Any(d => d.Kind == WorkDeliverableKind.PullRequest)))
        {
            try { prs = await sources.ListCardPullRequestsAsync(item.CardNumber, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning(e, "0091: PRs do card {Card} não lidos", item.CardNumber); }
        }
        return WorkDeliverableListing.Build(item, stored, links, prs);
    }

    // ================================================================ gravar

    public async Task<WorkDeliverableResponse> RecordAsync(long taskNumber, WorkDeliverableInput input, ExecutionActor actor, CancellationToken ct)
    {
        var item = await repo.GetItemByTaskAsync(taskNumber, ct) ?? throw TaskNotFound(taskNumber);
        return await SaveAsync(item, taskNumber, input, WorkDeliverableSource.Task, null, actor, ct);
    }

    public async Task<WorkDeliverableResponse> RecordForStepAsync(Guid planId, string stepKey, WorkDeliverableInput input, ExecutionActor actor, CancellationToken ct)
    {
        var (item, task) = await MirrorAsync(planId, stepKey, ct);
        return await SaveAsync(item, task.Number, input, WorkDeliverableSource.Task, null, actor, ct);
    }

    public async Task<WorkDeliverableResponse> RecordCodeAsync(long taskNumber, WorkDeliverableCodeUpload upload, ExecutionActor actor, CancellationToken ct)
    {
        var item = await repo.GetItemByTaskAsync(taskNumber, ct) ?? throw TaskNotFound(taskNumber);
        return await SaveCodeAsync(item, taskNumber, upload, actor, ct);
    }

    public async Task<WorkDeliverableResponse> RecordCodeForStepAsync(Guid planId, string stepKey, WorkDeliverableCodeUpload upload, ExecutionActor actor, CancellationToken ct)
    {
        var (item, task) = await MirrorAsync(planId, stepKey, ct);
        return await SaveCodeAsync(item, task.Number, upload, actor, ct);
    }

    public async Task<WorkDeliverableResponse?> RecordAutoAsync(string cardNumber, long? taskNumber, WorkDeliverableInput input, ExecutionActor actor,
        bool byClaude, CancellationToken ct)
    {
        var card = (cardNumber ?? string.Empty).Trim();
        try
        {
            var kind = WorkDeliverableKind.Normalize(input.Kind);
            var (item, task) = await ResolveTaskAsync(card, taskNumber, kind, byClaude, ct);
            if (item is null || task is null)
                return null;
            return await SaveAsync(item, task.Number, input, WorkDeliverableSource.Auto, card, actor, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "0091: entrega {Kind} do card {Card} não gravada", input.Kind, card);
            return null;
        }
    }

    /// <summary>
    /// Sub-tarefa que recebe a entrega do servidor: a informada (tem de ser do card) ou a em andamento no card de tipo
    /// compatível (a mais recente); pelo Claude, sem compatível, a única em andamento. Nenhuma → (null, null).
    /// </summary>
    private async Task<(WorkItem? Item, WorkItemTask? Task)> ResolveTaskAsync(string card, long? taskNumber, string kind, bool byClaude, CancellationToken ct)
    {
        if (taskNumber is > 0)
        {
            var owner = await repo.GetItemByTaskAsync(taskNumber.Value, ct) ?? throw TaskNotFound(taskNumber.Value);
            if (!string.Equals(owner.CardNumber, card, StringComparison.OrdinalIgnoreCase))
                throw new DomainException($"A sub-tarefa #{taskNumber} é do card {owner.CardNumber}, não do {card}.");
            return (owner, owner.Tasks.First(t => t.Number == taskNumber));
        }

        var running = (await repo.GetItemsWithRunningTaskAsync(card, ct))
            .SelectMany(i => i.Tasks.Where(t => t.Status == WorkTaskStatus.Running).Select(t => (Item: i, Task: t)))
            .ToList();
        var fit = running.Where(r => WorkDeliverableKind.Fits(kind, r.Task.Kind))
            .OrderByDescending(r => r.Task.StartedAt ?? DateTimeOffset.MinValue).ToList();
        if (fit.Count > 0)
            return fit[0];
        return byClaude && running.Count == 1 ? running[0] : (null, null);
    }

    private async Task<(WorkItem Item, WorkItemTask Task)> MirrorAsync(Guid planId, string stepKey, CancellationToken ct)
    {
        var key = ExecutionStep.NormalizeKey(stepKey);
        foreach (var item in (await repo.GetItemsByPlanAsync(planId, ct)).OrderBy(i => i.Number))
            if (item.Tasks.FirstOrDefault(t => t.StepKey == key) is { } task)
                return (item, task);
        throw new ExecutionPlanNotFoundException($"A etapa '{key}' não tem sub-tarefa de item de trabalho.");
    }

    private async Task<WorkDeliverableResponse> SaveAsync(WorkItem? item, long? taskNumber, WorkDeliverableInput input, string source,
        string? card, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var cardNumber = item?.CardNumber ?? card ?? throw new DomainException("Informe o card da entrega.");
        var kind = WorkDeliverableKind.Normalize(input.Kind);
        var key = WorkItemDeliverable.KeyOf(kind, input);
        var saved = await UpsertAsync(cardNumber, item, taskNumber, key, d => d.Apply(input, DateTimeOffset.UtcNow),
            () => new WorkItemDeliverable(cardNumber, item?.Id, taskNumber, input, source, actor.Name, DateTimeOffset.UtcNow), actor, ct);
        return WorkDeliverableResponse.From(saved);
    }

    private async Task<WorkDeliverableResponse> SaveCodeAsync(WorkItem item, long taskNumber, WorkDeliverableCodeUpload upload, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (string.IsNullOrWhiteSpace(upload.Repository) || string.IsNullOrWhiteSpace(upload.Branch))
            throw new DomainException("Informe repository e branch do código alterado.");
        var patch = upload.Patch ?? [];
        var files = UnifiedPatch.Parse(Encoding.UTF8.GetString(patch.Length > WorkItemDeliverable.MaxPatchBytes ? patch[..WorkItemDeliverable.MaxPatchBytes] : patch));
        var commits = upload.Commits.Where(c => !string.IsNullOrWhiteSpace(c.Sha)).Take(250).ToList();
        var truncated = patch.Length > WorkItemDeliverable.MaxPatchBytes;
        var data = new JsonObject
        {
            ["base"] = upload.Base,
            ["mergeBase"] = upload.MergeBase,
            ["commits"] = new JsonArray(commits.Select(c => (JsonNode)new JsonObject { ["sha"] = c.Sha.Trim(), ["title"] = c.Title?.Trim() }).ToArray()),
            ["files"] = files.Count,
            ["additions"] = files.Sum(f => f.Additions),
            ["deletions"] = files.Sum(f => f.Deletions),
            ["truncated"] = truncated,
            ["fileList"] = new JsonArray(files.Take(MaxFileList).Select(f => (JsonNode)new JsonObject
            {
                ["path"] = f.Path, ["status"] = f.Status, ["additions"] = f.Additions, ["deletions"] = f.Deletions,
            }).ToArray()),
        };
        var input = new WorkDeliverableInput
        {
            Kind = WorkDeliverableKind.Code,
            Repository = upload.Repository.Trim(),
            Branch = upload.Branch.Trim(),
            Target = string.IsNullOrWhiteSpace(upload.Base) ? null : upload.Base.Trim(),
            Sha = upload.Head ?? commits.LastOrDefault()?.Sha,
            Title = $"Código alterado em {upload.Repository.Trim()} ({upload.Branch.Trim()}): {files.Count} arquivo(s), +{files.Sum(f => f.Additions)} −{files.Sum(f => f.Deletions)}",
            Data = JsonDocument.Parse(data.ToJsonString()).RootElement.Clone(),
        };
        var key = WorkItemDeliverable.KeyOf(WorkDeliverableKind.Code, input);
        var saved = await UpsertAsync(item.CardNumber, item, taskNumber, key,
            d => { d.Apply(input, DateTimeOffset.UtcNow); if (patch.Length > 0) d.SetPatch(patch); },
            () =>
            {
                var d = new WorkItemDeliverable(item.CardNumber, item.Id, taskNumber, input, WorkDeliverableSource.Task, actor.Name, DateTimeOffset.UtcNow);
                if (patch.Length > 0) d.SetPatch(patch);
                return d;
            }, actor, ct);
        return WorkDeliverableResponse.From(saved);
    }

    private async Task<WorkItemDeliverable> UpsertAsync(string card, WorkItem? item, long? taskNumber, string key,
        Action<WorkItemDeliverable> update, Func<WorkItemDeliverable> create, ExecutionActor actor, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                repo.ClearTracking();
                var existing = await repo.FindForUpdateAsync(card, taskNumber, key, ct);
                WorkItemDeliverable d;
                if (existing is null)
                {
                    d = create();
                    repo.Add(d);
                }
                else
                {
                    d = existing;
                    update(d);
                }
                if (item is not null)
                    repo.AddLog(new WorkItemLog(item.Id, taskNumber, actor.Name, WorkItemLogKind.Info,
                        $"Entrega {(existing is null ? "registrada" : "atualizada")}: {d.Title}", DateTimeOffset.UtcNow));
                await repo.SaveChangesAsync(ct);
                if (item is not null)
                    await NotifyAsync(item, taskNumber, ct);
                return d;
            }
            catch (WorkItemDeliverableConflictException) when (attempt < Retries)
            {
                await Task.Delay(Random.Shared.Next(10, 50) * attempt, ct);
            }
        }
    }

    // ================================================================ diff

    public async Task<WorkDeliverableDiffResponse> DiffAsync(long number, string deliverableId, CancellationToken ct)
    {
        var item = await repo.GetItemAsync(number, ct) ?? throw NotFound(number);
        if (Guid.TryParse(deliverableId, out var id) && await repo.GetAsync(item.Id, id, ct) is { } stored)
        {
            if (stored.HasPatch && await repo.GetPatchAsync(stored.Id, ct) is { } patch)
                return new WorkDeliverableDiffResponse
                {
                    Source = WorkDeliverableDiffResponse.SourceSnapshot,
                    Repository = stored.Repository,
                    Base = stored.Target,
                    Head = stored.Branch,
                    Truncated = stored.PatchTruncated,
                    CompareUrl = CompareUrl(stored.Url, stored.Repository, stored.Target, stored.Branch),
                    Files = UnifiedPatch.Parse(Encoding.UTF8.GetString(patch)),
                };
            return await CompareAsync(stored.Kind, stored.Repository, stored.Target, stored.Branch, ct);
        }

        var d = (await ListAsync(item, ct)).FirstOrDefault(x => x.Id == deliverableId)
                ?? throw new ExecutionPlanNotFoundException($"Entrega não encontrada no item #{number}.");
        return await CompareAsync(d.Kind, d.Repository, d.Target, d.Branch, ct);
    }

    private async Task<WorkDeliverableDiffResponse> CompareAsync(string kind, string? repository, string? @base, string? head, CancellationToken ct)
    {
        if (!WorkDeliverableListing.CanCompare(kind, repository, head, @base) || sources is null)
            throw new ExecutionPlanNotFoundException("Esta entrega não tem alterações para mostrar.");
        var diff = await sources.CompareAsync(repository!, @base!, head!, ct);
        diff.Source = WorkDeliverableDiffResponse.SourceGithub;
        diff.Repository ??= repository;
        diff.Base ??= @base;
        diff.Head ??= head;
        return diff;
    }

    /// <summary>Compare no GitHub a partir de uma URL do mesmo repositório (PR/branch); sem URL, fica para o host.</summary>
    private static string? CompareUrl(string? url, string? repository, string? @base, string? head)
    {
        if (string.IsNullOrWhiteSpace(@base) || string.IsNullOrWhiteSpace(head) || string.IsNullOrWhiteSpace(url))
            return null;
        var i = url.IndexOf("github.com/", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var parts = url[(i + "github.com/".Length)..].Split('/');
        return parts.Length >= 2 ? $"https://github.com/{parts[0]}/{parts[1]}/compare/{@base}...{head}" : null;
    }

    // ================================================================ aviso do código

    public async Task<string?> MissingCodeWarningAsync(Guid planId, string stepKey, CancellationToken ct)
    {
        if (await repo.GetPlanPhaseAsync(planId, ct) != ExecutionPhase.Correction)
            return null;
        var key = ExecutionStep.NormalizeKey(stepKey);
        foreach (var item in await repo.GetItemsByPlanAsync(planId, ct))
        {
            if (item.Tasks.FirstOrDefault(t => t.StepKey == key) is not { Kind: WorkTaskKind.Code } task)
                continue;
            if ((await repo.ListAsync(item.Id, ct)).Any(d => d.TaskNumber == task.Number && d.Kind == WorkDeliverableKind.Code))
                return null;
            return $"A etapa '{key}' (sub-tarefa #{task.Number}) concluiu sem o código alterado registrado: rode " +
                   $"'prmake-plan.sh deliver-code <repositorio> --step {key}' no worktree para a tela mostrar a branch e o diff.";
        }
        return null;
    }

    // ================================================================ apoio

    private async Task NotifyAsync(WorkItem item, long? taskNumber, CancellationToken ct)
    {
        try
        {
            await realTime.NotifyGroupAsync(
                ExecutionPlanRealTimeEvents.Group(item.CardNumber),
                ExecutionPlanRealTimeEvents.EventWorkItemsUpdated,
                new
                {
                    cardNumber = item.CardNumber, number = item.Number, taskNumber,
                    action = ExecutionPlanRealTimeEvents.WorkItemActions.Deliverable, status = item.Status,
                },
                ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogDebug(e, "0091: aviso em tempo real da entrega não enviado");
        }
    }

    private static ExecutionPlanNotFoundException NotFound(long number) => new($"Item de trabalho #{number} não encontrado.");

    private static ExecutionPlanNotFoundException TaskNotFound(long number) => new($"Sub-tarefa #{number} não encontrada.");
}
