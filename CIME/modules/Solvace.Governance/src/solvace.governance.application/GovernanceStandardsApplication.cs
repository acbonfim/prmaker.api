using System.Text;
using System.Text.Json;
using solvace.governance.application.Contracts;
using solvace.governance.domain;
using solvace.governance.domain.Entities;
using solvace.governance.domain.Standards;

namespace solvace.governance.application;

public sealed record StandardFilter
{
    public string? Area { get; init; }
    public string? Status { get; init; }
    public string? Topic { get; init; }
    public string? Repository { get; init; }
    public string? Module { get; init; }
    public string? Search { get; init; }
}

public sealed record StandardUpsert
{
    public string? Key { get; init; }
    public StandardData Data { get; init; } = new();
}

public sealed record StandardCheckInput
{
    public string? Text { get; init; }
    public string Target { get; init; } = "sql";
    public string? Path { get; init; }
    public string? Repository { get; init; }
    public List<string>? Modules { get; init; }
    public int? Card { get; init; }
    public string Source { get; init; } = GovernanceSource.Skill;
}

public sealed record DraftApply
{
    /// <summary>Chaves aceitas; vazio/null = todas as que mudaram.</summary>
    public List<string>? Accept { get; init; }
}

public interface IGovernanceStandardsApplication
{
    Task<List<GovernanceStandard>> ListAsync(StandardFilter filter, CancellationToken ct = default);
    Task<GovernanceStandard> CreateAsync(StandardUpsert request, GovernanceActor actor, CancellationToken ct = default);
    Task<GovernanceStandard?> UpdateAsync(Guid id, StandardData data, GovernanceActor actor, CancellationToken ct = default);
    Task<bool?> DeleteAsync(Guid id, GovernanceActor actor, CancellationToken ct = default);
    Task<StandardCheckResult> CheckAsync(StandardCheckInput input, GovernanceActor actor, CancellationToken ct = default);
    Task<string> ForCardMarkdownAsync(string? repository, IReadOnlyCollection<string>? repositories, IReadOnlyCollection<string>? modules, CancellationToken ct = default);
    Task<GovernanceStandardDraft> CreateDraftAsync(string kind, string? version, string? url, IReadOnlyList<ParsedStandard> parsed,
        bool replacesSource, GovernanceActor actor, CancellationToken ct = default);
    Task<GovernanceStandardDraft?> GetDraftAsync(Guid id, CancellationToken ct = default);
    Task<(int Added, int Changed, int Removed)?> ApplyDraftAsync(Guid id, DraftApply request, GovernanceActor actor, CancellationToken ct = default);
}

/// <summary>
/// 0101: padrões de banco e de aplicação. Toda alteração (manual ou por importação) vira evento da governança com o
/// antes/depois; conferência com achado vira evento da categoria <c>standard</c> com os IDs dos padrões.
/// </summary>
public class GovernanceStandardsApplication(
    IGovernanceRepository repository,
    IGovernancePolicyProvider policy,
    IGovernanceEventSink sink,
    TimeProvider clock) : IGovernanceStandardsApplication
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<List<GovernanceStandard>> ListAsync(StandardFilter f, CancellationToken ct = default)
    {
        var all = await repository.GetStandardsAsync(ct);
        IEnumerable<GovernanceStandard> q = all;
        if (!string.IsNullOrWhiteSpace(f.Area)) q = q.Where(s => s.Area == f.Area);
        if (!string.IsNullOrWhiteSpace(f.Status)) q = q.Where(s => s.Status == f.Status);
        if (!string.IsNullOrWhiteSpace(f.Topic)) q = q.Where(s => s.Topic == f.Topic);
        if (!string.IsNullOrWhiteSpace(f.Repository) || !string.IsNullOrWhiteSpace(f.Module))
            q = q.Where(s => s.AppliesTo.AppliesTo(f.Repository, f.Module is null ? null : [f.Module]));
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var t = f.Search.Trim();
            q = q.Where(s => s.Code.Contains(t, StringComparison.OrdinalIgnoreCase) || s.Title.Contains(t, StringComparison.OrdinalIgnoreCase)
                             || s.TextPt.Contains(t, StringComparison.OrdinalIgnoreCase) || (s.TextEn?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        return q.ToList();
    }

    public async Task<GovernanceStandard> CreateAsync(StandardUpsert request, GovernanceActor actor, CancellationToken ct = default)
    {
        var key = GovernanceRule.NormalizeKey(request.Key);
        if ((await repository.GetStandardsAsync(ct)).Any(s => s.Key == key)) throw new DomainException($"Já existe um padrão com a chave {key}.");
        var standard = new GovernanceStandard(Guid.NewGuid(), key);
        standard.Update(request.Data with { Source = new StandardSource { Kind = "manual", Ref = key } }, actor.Name, clock.GetUtcNow());
        repository.AddStandard(standard);
        await repository.SaveChangesAsync(ct);
        Changed(actor, "POST Governance/standards", $"Criou o padrão {standard.Code}", null, Snapshot(standard));
        return standard;
    }

    public async Task<GovernanceStandard?> UpdateAsync(Guid id, StandardData data, GovernanceActor actor, CancellationToken ct = default)
    {
        var standard = await repository.GetStandardAsync(id, ct);
        if (standard is null) return null;
        var before = Snapshot(standard);
        // A origem é do importador: a tela não troca (a reimportação depende dela).
        standard.Update(data with { Source = standard.Source }, actor.Name, clock.GetUtcNow());
        await repository.SaveChangesAsync(ct);
        Changed(actor, "PUT Governance/standards/{id}", $"Alterou o padrão {standard.Code}", before, Snapshot(standard));
        return standard;
    }

    public async Task<bool?> DeleteAsync(Guid id, GovernanceActor actor, CancellationToken ct = default)
    {
        var standard = await repository.GetStandardAsync(id, ct);
        if (standard is null) return null;
        if (standard.Source.Kind != "manual") return false;
        var before = Snapshot(standard);
        repository.RemoveStandard(standard);
        await repository.SaveChangesAsync(ct);
        Changed(actor, "DELETE Governance/standards/{id}", $"Apagou o padrão {standard.Code}", before, null);
        return true;
    }

    public async Task<StandardCheckResult> CheckAsync(StandardCheckInput input, GovernanceActor actor, CancellationToken ct = default)
    {
        var p = await policy.GetAsync(ct);
        var result = StandardChecker.Check(new StandardCheckRequest
        {
            Text = input.Text, Target = input.Target, Path = input.Path, Repository = input.Repository, Modules = input.Modules
        }, p.Standards, p.Settings);
        if (result.Findings.Count > 0)
        {
            var detail = string.Join("\n", result.Findings.Take(15).Select(f => $"{f.Code}{(f.Line is { } l ? $" L{l}" : "")}: {f.Hint}"));
            sink.Enqueue(new GovernanceEventData
            {
                StartedAt = clock.GetUtcNow(),
                DurationMs = 0,
                UserId = actor.UserId,
                UserName = actor.Name,
                Role = actor.Roles.FirstOrDefault(),
                Source = GovernanceSource.IsValid(input.Source) ? input.Source : GovernanceSource.Skill,
                Action = $"standard:check:{input.Target}",
                Category = "standard",
                CardNumber = input.Card,
                Outcome = result.Outcome,
                Rules = result.Findings.Select(f => f.Key).Distinct().ToList(),
                Detail = $"{input.Repository ?? "-"} {input.Path ?? ""}\n{detail}"
            });
        }
        return result;
    }

    public async Task<string> ForCardMarkdownAsync(string? repository, IReadOnlyCollection<string>? repositories, IReadOnlyCollection<string>? modules,
        CancellationToken ct = default)
    {
        var p = await policy.GetAsync(ct);
        if (p.Settings.Mode == GovernanceMode.Off || p.Standards.Count == 0) return string.Empty;
        var repos = (repositories ?? []).Concat(repository is null ? [] : [repository]).Distinct().ToList();
        var list = p.Standards.Where(s => s.Status != StandardStatus.Deprecated
                                          && (repos.Count == 0 ? s.AppliesTo.Repositories.Count == 0 : repos.Any(r => s.AppliesTo.AppliesTo(r, modules))))
            .ToList();
        if (list.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine().AppendLine("## Padrões de banco e de aplicação (governança)");
        sb.AppendLine("Siga ao escrever SQL, migração ou código; confira com `prmake_standards_check` (ou `POST Governance/standards/check`) antes de gravar. " +
                      "Legado no `dbo` mantém os nomes atuais. (proposto) = aguarda validação, siga quando der.");
        foreach (var area in list.GroupBy(s => s.Area).OrderBy(g => g.Key))
        {
            sb.AppendLine().AppendLine(area.Key == StandardArea.Database ? "### Banco de dados" : "### Aplicação");
            foreach (var s in area.OrderBy(s => s.Order))
            {
                var tag = s.Status == StandardStatus.Proposed ? " (proposto)" : s.EffectiveEffect == GovernanceEffect.Block ? " (barra)" : s.EffectiveEffect == GovernanceEffect.Warn ? " (avisa)" : "";
                var ex = s.Examples.Good.Count > 0 ? $" Ex.: `{string.Join("`, `", s.Examples.Good.Take(2))}`" : "";
                sb.AppendLine($"- **{s.Code}**{tag} {s.TextPt}{ex}");
            }
        }
        return sb.ToString();
    }

    public async Task<GovernanceStandardDraft> CreateDraftAsync(string kind, string? version, string? url, IReadOnlyList<ParsedStandard> parsed,
        bool replacesSource, GovernanceActor actor, CancellationToken ct = default)
    {
        var existing = (await repository.GetStandardsAsync(ct)).ToDictionary(s => s.Key);
        var items = new List<StandardDraftItem>();
        foreach (var p in parsed)
        {
            if (!existing.TryGetValue(p.Key, out var cur))
                items.Add(new StandardDraftItem { Key = p.Key, Change = "added", Data = p.Data });
            else if (cur.Source.Hash != p.Hash || cur.Source.Version != p.Data.Source?.Version)
                items.Add(new StandardDraftItem
                {
                    Key = p.Key, Change = cur.Source.Hash != p.Hash ? "changed" : "same", Data = p.Data,
                    PreviousTitle = cur.Title, PreviousTextPt = cur.TextPt, PreviousStatus = cur.Status
                });
        }
        // Itens desta origem que sumiram da fonte → descontinuar (só quando a importação cobre a origem inteira).
        if (replacesSource)
        {
            var keys = parsed.Select(p => p.Key).ToHashSet();
            items.AddRange(existing.Values.Where(s => s.Source.Kind == kind && s.Status != StandardStatus.Deprecated && !keys.Contains(s.Key))
                .Select(s => new StandardDraftItem { Key = s.Key, Change = "removed", PreviousTitle = s.Title, PreviousTextPt = s.TextPt, PreviousStatus = s.Status }));
        }
        var draft = new GovernanceStandardDraft(Guid.NewGuid(), kind, version, url, items, actor.Name, clock.GetUtcNow());
        repository.AddDraft(draft);
        await repository.SaveChangesAsync(ct);
        return draft;
    }

    public Task<GovernanceStandardDraft?> GetDraftAsync(Guid id, CancellationToken ct = default) => repository.GetDraftAsync(id, ct);

    public async Task<(int Added, int Changed, int Removed)?> ApplyDraftAsync(Guid id, DraftApply request, GovernanceActor actor, CancellationToken ct = default)
    {
        var draft = await repository.GetDraftAsync(id, ct);
        if (draft is null) return null;
        var accept = request.Accept is { Count: > 0 } a ? a.ToHashSet() : null;
        var existing = (await repository.GetStandardsAsync(ct)).ToDictionary(s => s.Key);
        var now = clock.GetUtcNow();
        int added = 0, changed = 0, removed = 0;
        foreach (var item in draft.Items.Where(i => accept is null || accept.Contains(i.Key)))
        {
            switch (item.Change)
            {
                case "added" when item.Data is not null && !existing.ContainsKey(item.Key):
                    var s = new GovernanceStandard(Guid.NewGuid(), item.Key);
                    s.Update(item.Data, actor.Name, now);
                    repository.AddStandard(s);
                    added++;
                    break;
                case "changed" or "same" when item.Data is not null && existing.TryGetValue(item.Key, out var cur):
                    // Texto, exemplos, origem e conferência vêm da fonte; efeito e "aplica-se" ficam como o admin deixou.
                    // Estado acompanha a fonte (ex.: (NEW) que o DBA validou vira vigente), salvo se o admin descontinuou.
                    var status = cur.Status == StandardStatus.Deprecated ? cur.Status : item.Data.Status;
                    cur.Update(item.Data with { Status = status, Effect = cur.Effect, AppliesTo = cur.AppliesTo }, actor.Name, now);
                    changed++;
                    break;
                case "removed" when existing.TryGetValue(item.Key, out var gone):
                    gone.Update(new StandardData
                    {
                        Code = gone.Code, Area = gone.Area, Topic = gone.Topic, Title = gone.Title, TextPt = gone.TextPt, TextEn = gone.TextEn,
                        Examples = gone.Examples, AppliesTo = gone.AppliesTo, Source = gone.Source, Status = StandardStatus.Deprecated,
                        Effect = GovernanceEffect.Monitor, Check = gone.Check, Order = gone.Order
                    }, actor.Name, now);
                    removed++;
                    break;
            }
        }
        draft.MarkApplied(actor.Name, now);
        await repository.SaveChangesAsync(ct);
        Changed(actor, "POST Governance/standards/import/{id}/apply",
            $"Importou padrões ({draft.Kind} {draft.Version}): {added} novos, {changed} alterados, {removed} descontinuados", null, null);
        return (added, changed, removed);
    }

    private void Changed(GovernanceActor actor, string action, string summary, object? before, object? after)
    {
        policy.Invalidate();
        sink.Enqueue(new GovernanceEventData
        {
            StartedAt = clock.GetUtcNow(),
            DurationMs = 0,
            UserId = actor.UserId,
            UserName = actor.Name,
            Role = actor.Roles.FirstOrDefault(),
            Source = GovernanceSource.IsValid(actor.Source) ? actor.Source : GovernanceSource.Screen,
            Action = action,
            Category = "governance",
            Outcome = GovernanceOutcome.Ok,
            Detail = $"{summary}\nantes: {(before is null ? "-" : JsonSerializer.Serialize(before, Json))}\ndepois: {(after is null ? "-" : JsonSerializer.Serialize(after, Json))}"
        });
    }

    private static object Snapshot(GovernanceStandard s) => new { s.Key, s.Code, s.Status, s.Effect, s.AppliesTo, s.Check, s.Title };
}
