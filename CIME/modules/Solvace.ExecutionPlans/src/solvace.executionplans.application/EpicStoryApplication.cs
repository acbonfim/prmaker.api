using System.Text;
using Cime.BuildingBlocks.RealTime;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.RealTime;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

/// <summary>
/// 0099 (ajustes 2): histórias e bugs do épico (aba "Histórias" da Mesa de gestão). Rascunho no PRMake que vira card do DevOps uma
/// vez só — a trava (<see cref="EpicStory.BeginConversion"/>) é gravada com xmin antes de o host criar o card, então duas conversões
/// ao mesmo tempo não criam dois cards. Tempo real: <c>epicsUpdated</c> action <c>stories</c> no grupo <c>epics</c> (sinal).
/// </summary>
public class EpicStoryApplication(IEpicStoryRepository stories, IEpicRepository epics, IRealTimeNotifier realTime) : IEpicStoryApplication
{
    private const int Retries = 4;
    private const int ContextStories = 60;

    // ================================================================ leitura

    public async Task<IReadOnlyList<EpicStoryResponse>> ListAsync(long epicNumber, string? status, CancellationToken ct)
    {
        var epic = await EpicAsync(epicNumber, ct);
        var now = DateTimeOffset.UtcNow;
        return (await stories.ListByEpicAsync(epic.Id, EpicStoryStatus.ParseFilter(status), ct)).Select(s => EpicStoryResponse.From(s, now)).ToList();
    }

    public async Task<EpicStoryResponse> GetAsync(long number, CancellationToken ct) =>
        await FindAsync(number, ct) ?? throw NotFound(number);

    public async Task<EpicStoryResponse?> FindAsync(long number, CancellationToken ct)
    {
        if (number <= 0) return null;
        var story = await stories.FindByNumberAsync(number, ct);
        return story is null ? null : EpicStoryResponse.From(story, DateTimeOffset.UtcNow);
    }

    public async Task<string> ContextMarkdownAsync(long epicNumber, CancellationToken ct)
    {
        var epic = await EpicAsync(epicNumber, ct);
        var all = await stories.ListByEpicAsync(epic.Id, null, ct);
        return ContextMarkdown(epicNumber, all, DateTimeOffset.UtcNow);
    }

    /// <summary>Seção "Histórias e bugs" do contexto do especialista (rascunhos × convertidas; descartadas só contadas).</summary>
    public static string ContextMarkdown(long epicNumber, IReadOnlyCollection<EpicStory> all, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        var drafts = all.Where(s => s.Status is EpicStoryStatus.Draft or EpicStoryStatus.Ready).OrderByDescending(s => s.Number).ToList();
        var converted = all.Where(s => s.IsConverted).OrderByDescending(s => s.ConvertedAt).ToList();
        var discarded = all.Count(s => s.Status == EpicStoryStatus.Discarded);
        sb.AppendLine($"## Histórias e bugs do épico no PRMake ({drafts.Count} rascunho(s), {converted.Count} convertida(s))");
        sb.AppendLine("Rascunhos escritos no PRMake (aba Histórias da Mesa): prmake_epic_stories(epic) lista, prmake_epic_story_save cria/edita; " +
                      "prmake_epic_story_convert(number) cria o card no DevOps — SÓ quando a pessoa pedir. Convertida não se edita mais (edite o card).");
        if (all.Count == 0)
        {
            sb.AppendLine("Nenhuma história ainda.");
            return sb.ToString();
        }
        if (drafts.Count > 0)
        {
            sb.AppendLine("### Rascunhos (ainda não são cards)");
            foreach (var s in drafts.Take(ContextStories))
            {
                var extra = new List<string> { s.Status == EpicStoryStatus.Ready ? "pronta" : "rascunho" };
                if (s.IsConverting(now)) extra.Add("convertendo agora");
                if (s.Priority is { } p) extra.Add($"prioridade {p}");
                if (!string.IsNullOrWhiteSpace(s.Severity)) extra.Add($"severidade {s.Severity}");
                if (s.Tags.Count > 0) extra.Add("tags " + string.Join(", ", s.Tags));
                var missing = s.Type == EpicStoryType.Bug
                    ? string.IsNullOrWhiteSpace(s.ReproSteps) ? " · sem passos de reprodução" : string.Empty
                    : string.IsNullOrWhiteSpace(s.AcceptanceCriteria) ? " · sem critérios de aceite" : string.Empty;
                sb.AppendLine($"- #{s.Number} [{TypeText(s.Type)}] {s.Title} ({string.Join(" · ", extra)}){missing}");
            }
            if (drafts.Count > ContextStories) sb.AppendLine($"- … mais {drafts.Count - ContextStories} — prmake_epic_stories({epicNumber}).");
        }
        if (converted.Count > 0)
        {
            sb.AppendLine("### Convertidas (já são cards do DevOps)");
            foreach (var s in converted.Take(ContextStories))
                sb.AppendLine($"- #{s.Number} [{TypeText(s.Type)}] {s.Title} → card {s.CardId} (por {s.ConvertedByName}, {s.ConvertedAt:dd/MM})");
            if (converted.Count > ContextStories) sb.AppendLine($"- … mais {converted.Count - ContextStories} — prmake_epic_stories({epicNumber}).");
        }
        if (discarded > 0)
            sb.AppendLine($"- Descartadas: {discarded} (prmake_epic_stories({epicNumber}, status: \"all\")).");
        return sb.ToString();
    }

    // ================================================================ gravação

    public async Task<EpicStoryResponse> CreateAsync(long epicNumber, SaveEpicStoryRequest req, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var epic = await EpicAsync(epicNumber, ct);
        var now = DateTimeOffset.UtcNow;
        var story = new EpicStory(epic.Id, epic.Number, req.Type, req.Title, req.Description, req.AcceptanceCriteria, req.ReproSteps, req.Priority,
            req.Severity, req.Tags, req.Fields, req.Status, actor.UserId, actor.Name, now);
        stories.Add(story);
        await stories.SaveChangesAsync(ct);
        await NotifyAsync(story.EpicNumber, story.Number, ct);
        return EpicStoryResponse.From(story, now);
    }

    public async Task<EpicStoryResponse> UpdateAsync(long number, SaveEpicStoryRequest req, ExecutionActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        var story = await MutateAsync(number, s => s.Update(req.Type, req.Title, req.Description, req.AcceptanceCriteria, req.ReproSteps,
            req.Priority, req.Severity, req.Tags, req.Fields, req.Status, DateTimeOffset.UtcNow), ct);
        await NotifyAsync(story.EpicNumber, story.Number, ct);
        return EpicStoryResponse.From(story, DateTimeOffset.UtcNow);
    }

    public async Task<EpicStoryResponse> DiscardAsync(long number, ExecutionActor actor, CancellationToken ct)
    {
        var changed = false;
        var story = await MutateAsync(number, s => changed = s.Discard(DateTimeOffset.UtcNow), ct);
        if (changed) await NotifyAsync(story.EpicNumber, story.Number, ct);
        return EpicStoryResponse.From(story, DateTimeOffset.UtcNow);
    }

    public async Task<EpicStoryResponse> BeginConversionAsync(long number, ExecutionActor actor, CancellationToken ct)
    {
        // Conflito de xmin = outra conversão gravou a trava ao mesmo tempo: a nova tentativa relê e cai em "converting".
        var story = await MutateAsync(number, s => s.BeginConversion(DateTimeOffset.UtcNow), ct);
        await NotifyAsync(story.EpicNumber, story.Number, ct);
        return EpicStoryResponse.From(story, DateTimeOffset.UtcNow);
    }

    public async Task<EpicStoryResponse> CompleteConversionAsync(long number, string cardId, ExecutionActor actor, CancellationToken ct)
    {
        var story = await MutateAsync(number, s => s.CompleteConversion(cardId, actor.Name, DateTimeOffset.UtcNow), ct);
        await NotifyAsync(story.EpicNumber, story.Number, ct);
        return EpicStoryResponse.From(story, DateTimeOffset.UtcNow);
    }

    public async Task AbortConversionAsync(long number, CancellationToken ct)
    {
        try
        {
            var story = await MutateAsync(number, s => s.AbortConversion(), ct);
            await NotifyAsync(story.EpicNumber, story.Number, ct);
        }
        catch (Exception e) when (e is ExecutionPlanNotFoundException or ExecutionPlanConcurrencyException)
        {
            // Best-effort: a trava vence sozinha em EpicStory.ConversionLock.
        }
    }

    // ================================================================ internos

    /// <summary>Carrega, muda e salva com novas tentativas em conflito de xmin.</summary>
    private async Task<EpicStory> MutateAsync(long number, Action<EpicStory> change, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var story = await stories.GetByNumberAsync(number, ct) ?? throw NotFound(number);
            change(story);
            try
            {
                await stories.SaveChangesAsync(ct);
                return story;
            }
            catch (ExecutionPlanConcurrencyException) when (attempt < Retries)
            {
                stories.Detach();
                await Task.Delay(Random.Shared.Next(10, 40) * attempt, ct);
            }
        }
    }

    private async Task<Epic> EpicAsync(long epicNumber, CancellationToken ct) =>
        (await epics.ListByNumbersAsync([epicNumber], ct)).FirstOrDefault()
        ?? throw new ExecutionPlanNotFoundException($"Épico #{epicNumber} não encontrado.");

    private async Task NotifyAsync(long epicNumber, long story, CancellationToken ct)
    {
        try
        {
            await realTime.NotifyGroupAsync(ExecutionPlanRealTimeEvents.EpicsGroup, ExecutionPlanRealTimeEvents.EventEpicsUpdated,
                new { number = epicNumber, key = EpicKey.Of(epicNumber), action = ExecutionPlanRealTimeEvents.EpicActions.Stories, story }, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException) { }
    }

    private static string TypeText(string type) => type == EpicStoryType.Bug ? "Bug" : "US";

    private static ExecutionPlanNotFoundException NotFound(long number) => new($"História #{number} não encontrada.");
}
