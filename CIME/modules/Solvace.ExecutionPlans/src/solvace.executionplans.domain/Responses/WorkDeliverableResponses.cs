using System.Text.Json;
using System.Text.Json.Nodes;
using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

/// <summary>0091: uma entrega do item/sub-tarefa como a tela e o MCP mostram (JSON camelCase).</summary>
public class WorkDeliverableResponse
{
    /// <summary>Guid da entrega gravada, ou sintético (<c>out:57:pr:0</c>, <c>link:&lt;guid&gt;</c>, <c>cardpr:repo#123</c>).</summary>
    public string Id { get; set; } = string.Empty;
    public long? TaskNumber { get; set; }
    public string Kind { get; set; } = WorkDeliverableKind.Doc;
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Status { get; set; }
    public string? Repository { get; set; }
    public string? Branch { get; set; }
    public string? Target { get; set; }
    public int? Number { get; set; }
    public string? Sha { get; set; }
    public string? Text { get; set; }
    public JsonElement Data { get; set; }
    public string Source { get; set; } = WorkDeliverableSource.Auto;
    public bool Reverted { get; set; }
    public bool HasDiff { get; set; }
    public DateTimeOffset At { get; set; }

    public static WorkDeliverableResponse From(WorkItemDeliverable d) => new()
    {
        Id = d.Id.ToString(),
        TaskNumber = d.TaskNumber,
        Kind = d.Kind,
        Title = d.Title,
        Url = d.Url,
        Status = d.Status,
        Repository = d.Repository,
        Branch = d.Branch,
        Target = d.Target,
        Number = d.Number,
        Sha = d.Sha,
        Text = d.Text,
        Data = d.DataElement,
        Source = d.Source,
        HasDiff = d.HasPatch || WorkDeliverableListing.CanCompare(d.Kind, d.Repository, d.Branch, d.Target),
        At = d.UpdatedAt,
    };
}

/// <summary>0091: diff de uma entrega — a fotografia gravada (código) ou o compare ao vivo no GitHub (PR/branch).</summary>
public class WorkDeliverableDiffResponse
{
    public const string SourceSnapshot = "snapshot";
    public const string SourceGithub = "github";

    public string Source { get; set; } = SourceSnapshot;
    public string? Repository { get; set; }
    public string? Base { get; set; }
    public string? Head { get; set; }
    public bool Truncated { get; set; }
    public string? CompareUrl { get; set; }
    public List<WorkDeliverableDiffFile> Files { get; set; } = [];
    public string? Error { get; set; }
}

public class WorkDeliverableDiffFile
{
    public string Path { get; set; } = string.Empty;
    public string? PreviousPath { get; set; }
    /// <summary>modified | added | removed | renamed</summary>
    public string Status { get; set; } = "modified";
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public string? Patch { get; set; }
}

/// <summary>0091: PR registrado no card (para achar os PRs de itens anteriores à 0091 e o estado atual).</summary>
public record DeliverableCardPr(string Repository, int Number, string Url, string BaseBranch, string HeadBranch, string Status, DateTimeOffset CreatedAt);

/// <summary>
/// 0091: monta as entregas do item — as gravadas mais as que dá para tirar do que já existe (itens anteriores à 0091 e
/// sub-tarefas espelho do plano): <c>output</c>/<c>links</c> da sub-tarefa, links da etapa do plano e, para sub-tarefa de PR
/// sem nada, o PR do card no mesmo repositório aberto enquanto ela rodava. A mesma coisa vinda de dois lugares aparece uma
/// vez (fica a de fonte mais forte; o estado vem do mais atual).
/// </summary>
public static class WorkDeliverableListing
{
    /// <summary>Folga da janela da sub-tarefa para achar o PR do card aberto por ela.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromMinutes(3);

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public static bool CanCompare(string kind, string? repository, string? branch, string? target) =>
        kind is WorkDeliverableKind.PullRequest or WorkDeliverableKind.Branch or WorkDeliverableKind.Code
        && !string.IsNullOrWhiteSpace(repository) && !string.IsNullOrWhiteSpace(branch) && !string.IsNullOrWhiteSpace(target);

    public static List<WorkDeliverableResponse> Build(WorkItem item, IReadOnlyList<WorkItemDeliverable> stored,
        IReadOnlyList<ExecutionLink> planLinks, IReadOnlyList<DeliverableCardPr> cardPrs)
    {
        var tasks = item.Tasks.OrderBy(t => t.Order).ToList();
        var byStep = tasks.Where(t => t.StepKey is not null).GroupBy(t => t.StepKey!).ToDictionary(g => g.Key, g => g.First());
        var list = new List<WorkDeliverableResponse>();

        list.AddRange(stored.Select(WorkDeliverableResponse.From));

        if (item.PlanId is not null)
        {
            foreach (var link in planLinks.OrderBy(l => l.CreatedAt))
            {
                if (!byStep.TryGetValue(link.StepKey, out var task))
                    continue;
                Add(list, FromPlanLink(link, task.Number));
            }
        }

        foreach (var task in tasks)
            foreach (var d in FromOutput(task))
                Add(list, d);

        foreach (var link in item.Links)
            Add(list, FromItemLink(item, link));

        foreach (var d in FromCardPrs(item, tasks, list, cardPrs))
            Add(list, d);

        // Estado atual do PR pelo registro do card (o link do plano/gravação pode ter ficado para trás).
        foreach (var d in list.Where(d => d.Kind == WorkDeliverableKind.PullRequest))
        {
            var pr = cardPrs.FirstOrDefault(p => SamePr(d, p.Repository, p.Number, p.Url));
            if (pr is null) continue;
            if (d.Source != WorkDeliverableSource.Plan || d.Status is null)
                d.Status = pr.Status;
            d.Branch ??= pr.HeadBranch;
            d.Target ??= pr.BaseBranch;
            d.HasDiff = d.HasDiff || CanCompare(d.Kind, d.Repository, d.Branch, d.Target);
        }

        var status = tasks.ToDictionary(t => t.Number, t => t.Status);
        foreach (var d in list)
            d.Reverted = d.TaskNumber is { } n && status.TryGetValue(n, out var s) && s == WorkTaskStatus.Reverted
                         || item.Status == WorkItemStatus.Reverted;

        var order = tasks.Select((t, i) => (t.Number, i)).ToDictionary(x => x.Number, x => x.i);
        return list
            .OrderBy(d => d.TaskNumber is { } n && order.TryGetValue(n, out var i) ? i : int.MaxValue)
            .ThenBy(d => d.At)
            .ToList();
    }

    // ------------------------------------------------------------------ fontes

    private static WorkDeliverableResponse FromPlanLink(ExecutionLink link, long taskNumber)
    {
        var kind = link.Kind switch
        {
            ExecutionLinkKind.PullRequest => WorkDeliverableKind.PullRequest,
            ExecutionLinkKind.Ticket => WorkDeliverableKind.Ticket,
            _ => WorkDeliverableKind.Doc,
        };
        var data = new JsonObject();
        if (kind == WorkDeliverableKind.PullRequest)
        {
            if (link.ChecksStatus is { } c) data["checks"] = c;
            if (link.ChecksFailed is { } f) data["checksFailed"] = f;
            if (link.Approvals is { } a) data["approvals"] = a;
            if (link.ChangesRequested is { } cr) data["changesRequested"] = cr;
        }
        return new WorkDeliverableResponse
        {
            Id = $"link:{link.Id}",
            TaskNumber = taskNumber,
            Kind = kind,
            Title = link.Title ?? (kind == WorkDeliverableKind.PullRequest && link.PullRequestNumber is { } n
                ? $"PR #{n}{(link.Repository is { } r ? $" {r}" : string.Empty)}{(link.TargetBranch is { } t ? $" → {t}" : string.Empty)}"
                : link.Url),
            Url = link.Url,
            Status = link.Status,
            Repository = link.Repository,
            Target = link.TargetBranch,
            Number = link.PullRequestNumber,
            Data = ToElement(data),
            Source = WorkDeliverableSource.Plan,
            At = link.CreatedAt,
        };
    }

    /// <summary>
    /// <c>output</c> da sub-tarefa (convenção da skill executar-item-de-trabalho): <c>pr, number, repository, branch, target,
    /// commit, commits, ticket, from/to, hours</c>.
    /// </summary>
    public static IEnumerable<WorkDeliverableResponse> FromOutput(WorkItemTask task)
    {
        var o = task.OutputElement;
        if (o.ValueKind != JsonValueKind.Object)
            yield break;
        var at = task.CompletedAt ?? task.StartedAt ?? task.UpdatedAt;
        var repo = Str(o, "repository") ?? Str(o, "repo") ?? task.Params.GetValueOrDefault("repository");
        var branch = Str(o, "branch") ?? Str(o, "source");
        var target = Str(o, "target") ?? Str(o, "base");
        var i = 0;
        WorkDeliverableResponse New(string kind) => new()
        {
            Id = $"out:{task.Number}:{kind}:{i++}", TaskNumber = task.Number, Kind = kind, Repository = repo,
            Source = WorkDeliverableSource.Task, Data = EmptyObject, At = at,
        };

        if (Str(o, "pr") is { } prUrl && LooksLikeUrl(prUrl))
        {
            var d = New(WorkDeliverableKind.PullRequest);
            d.Url = prUrl;
            d.Number = Int(o, "number") ?? PrNumberFromUrl(prUrl);
            d.Branch = branch;
            d.Target = target;
            d.Title = $"PR{(d.Number is { } n ? $" #{n}" : string.Empty)}{(repo is null ? string.Empty : $" {repo}")}" +
                      (branch is null ? string.Empty : $": {branch}{(target is null ? string.Empty : $" → {target}")}");
            if (Bool(o, "alreadyExisted") is { } ex)
                d.Data = ToElement(new JsonObject { ["alreadyExisted"] = ex });
            d.HasDiff = CanCompare(d.Kind, repo, branch, target);
            yield return d;
        }
        else if (branch is not null && task.Kind is WorkTaskKind.Branch or WorkTaskKind.CherryPick or WorkTaskKind.Code)
        {
            var d = New(WorkDeliverableKind.Branch);
            d.Branch = branch;
            d.Target = target ?? Str(o, "from");
            d.Sha = Str(o, "commit");
            d.Title = $"Branch {branch}{(repo is null ? string.Empty : $" ({repo})")}";
            var commits = Commits(o);
            if (commits.Count > 0)
                d.Data = ToElement(new JsonObject { ["commits"] = new JsonArray(commits.Select(c => (JsonNode)new JsonObject { ["sha"] = c.Sha, ["title"] = c.Title }).ToArray()) });
            d.HasDiff = CanCompare(d.Kind, repo, branch, d.Target);
            yield return d;
        }
        else if (Str(o, "commit") is { } sha)
        {
            var d = New(WorkDeliverableKind.Commit);
            d.Sha = sha;
            d.Branch = branch;
            d.Title = $"Commit {sha[..Math.Min(7, sha.Length)]}";
            yield return d;
        }

        if (Str(o, "ticket") is { } ticket)
        {
            var d = New(WorkDeliverableKind.Ticket);
            d.Url = LooksLikeUrl(ticket) ? ticket : null;
            d.Text = d.Url is null ? ticket : null;
            d.Title = "Chamado";
            if (Str(o, "system") is { } system)
                d.Data = ToElement(new JsonObject { ["system"] = system });
            yield return d;
        }

        if (Str(o, "to") is { } to && task.Kind is WorkTaskKind.DevOpsState or WorkTaskKind.DevOpsAction)
        {
            var d = New(WorkDeliverableKind.DevOpsState);
            var from = Str(o, "from");
            d.Title = from is null ? $"Card movido para {to}" : $"Card movido de {from} para {to}";
            d.Data = ToElement(new JsonObject { ["from"] = from, ["to"] = to, ["action"] = Str(o, "action") });
            yield return d;
        }

        if (Str(o, "hours") is { } hours)
        {
            var d = New(WorkDeliverableKind.Hours);
            d.Text = hours;
            d.Title = $"{hours} h lançadas";
            d.Data = ToElement(new JsonObject { ["hours"] = hours });
            yield return d;
        }
    }

    private static WorkDeliverableResponse FromItemLink(WorkItem item, WorkItemLink link)
    {
        var kind = link.Kind switch
        {
            WorkItemLinkKind.PullRequest => WorkDeliverableKind.PullRequest,
            WorkItemLinkKind.Branch => WorkDeliverableKind.Branch,
            WorkItemLinkKind.Commit => WorkDeliverableKind.Commit,
            WorkItemLinkKind.Timeline => WorkDeliverableKind.Timeline,
            _ => WorkDeliverableKind.Doc,
        };
        return new WorkDeliverableResponse
        {
            Id = $"itemlink:{link.Url}",
            Kind = kind,
            Title = string.IsNullOrWhiteSpace(link.Title) ? link.Url : link.Title,
            Url = link.Url,
            Number = kind == WorkDeliverableKind.PullRequest ? PrNumberFromUrl(link.Url) : null,
            Data = EmptyObject,
            Source = WorkDeliverableSource.Task,
            At = item.UpdatedAt,
        };
    }

    /// <summary>PR do card no repositório da sub-tarefa de PR, aberto enquanto ela rodava — itens sem nada gravado.</summary>
    private static IEnumerable<WorkDeliverableResponse> FromCardPrs(WorkItem item, List<WorkItemTask> tasks,
        List<WorkDeliverableResponse> known, IReadOnlyList<DeliverableCardPr> cardPrs)
    {
        var prTasks = tasks.Where(t => t.Kind == WorkTaskKind.PullRequest
                                       && t.Status is not (WorkTaskStatus.Pending or WorkTaskStatus.Cancelled or WorkTaskStatus.Skipped)
                                       && !known.Any(k => k.TaskNumber == t.Number && k.Kind == WorkDeliverableKind.PullRequest))
            .ToList();
        if (prTasks.Count == 0)
            yield break;
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in prTasks)
        {
            var repo = task.Params.GetValueOrDefault("repository");
            if (string.IsNullOrWhiteSpace(repo))
                continue;
            var from = (task.StartedAt ?? item.StartedAt ?? item.CreatedAt) - Slack;
            var to = (task.CompletedAt ?? DateTimeOffset.MaxValue - Slack) + Slack;
            foreach (var pr in cardPrs.Where(p => string.Equals(p.Repository, repo, StringComparison.OrdinalIgnoreCase)
                                                  && p.CreatedAt >= from && p.CreatedAt <= to))
            {
                if (known.Any(k => SamePr(k, pr.Repository, pr.Number, pr.Url)) || !taken.Add(pr.Url))
                    continue;
                yield return new WorkDeliverableResponse
                {
                    Id = $"cardpr:{pr.Repository}#{pr.Number}",
                    TaskNumber = task.Number,
                    Kind = WorkDeliverableKind.PullRequest,
                    Title = $"PR #{pr.Number} {pr.Repository}: {pr.HeadBranch} → {pr.BaseBranch}",
                    Url = pr.Url,
                    Status = pr.Status,
                    Repository = pr.Repository,
                    Branch = pr.HeadBranch,
                    Target = pr.BaseBranch,
                    Number = pr.Number,
                    Data = EmptyObject,
                    Source = WorkDeliverableSource.Deduced,
                    HasDiff = CanCompare(WorkDeliverableKind.PullRequest, pr.Repository, pr.HeadBranch, pr.BaseBranch),
                    At = pr.CreatedAt,
                };
            }
        }
    }

    // ------------------------------------------------------------------ juntar

    /// <summary>A mesma entrega vinda de outra fonte não repete: completa a que já está (sub-tarefa, branch, destino, estado).</summary>
    private static void Add(List<WorkDeliverableResponse> list, WorkDeliverableResponse d)
    {
        var same = list.FirstOrDefault(x => Same(x, d));
        if (same is null)
        {
            list.Add(d);
            return;
        }
        same.TaskNumber ??= d.TaskNumber;
        same.Branch ??= d.Branch;
        same.Target ??= d.Target;
        same.Repository ??= d.Repository;
        same.Number ??= d.Number;
        same.Url ??= d.Url;
        if (d.Source == WorkDeliverableSource.Plan && d.Status is not null)
            same.Status = d.Status;
        else
            same.Status ??= d.Status;
        if (same.Data.ValueKind != JsonValueKind.Object || !same.Data.EnumerateObject().Any())
            same.Data = d.Data;
        same.HasDiff = same.HasDiff || d.HasDiff || CanCompare(same.Kind, same.Repository, same.Branch, same.Target);
    }

    private static bool Same(WorkDeliverableResponse a, WorkDeliverableResponse b)
    {
        if (a.Kind != b.Kind)
            return false;
        if (a.Kind == WorkDeliverableKind.PullRequest)
            return SamePr(a, b.Repository, b.Number, b.Url);
        if (a.Kind is WorkDeliverableKind.Branch or WorkDeliverableKind.Code && a.Branch is not null && b.Branch is not null)
            return Eq(a.Branch, b.Branch) && (a.Repository is null || b.Repository is null || Eq(a.Repository, b.Repository));
        if (a.Kind == WorkDeliverableKind.Commit && a.Sha is not null && b.Sha is not null)
            return a.Sha.StartsWith(b.Sha, StringComparison.OrdinalIgnoreCase) || b.Sha.StartsWith(a.Sha, StringComparison.OrdinalIgnoreCase);
        return a.Url is not null && b.Url is not null && SameUrl(a.Url, b.Url);
    }

    private static bool SamePr(WorkDeliverableResponse d, string? repository, int? number, string? url)
    {
        if (d.Url is not null && url is not null && SameUrl(d.Url, url))
            return true;
        var n = d.Number ?? (d.Url is null ? null : PrNumberFromUrl(d.Url));
        var m = number ?? (url is null ? null : PrNumberFromUrl(url));
        return n is not null && n == m && d.Repository is not null && repository is not null && Eq(d.Repository, repository);
    }

    private static bool SameUrl(string a, string b) =>
        string.Equals(a.Trim().TrimEnd('/'), b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static bool Eq(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ JSON

    public static int? PrNumberFromUrl(string url)
    {
        var i = url.IndexOf("/pull/", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var digits = new string(url[(i + 6)..].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : null;
    }

    private static bool LooksLikeUrl(string v) => v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || v.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static string? Str(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var v)) return null;
        var s = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static int? Int(JsonElement o, string name) => Str(o, name) is { } s && int.TryParse(s, out var n) && n > 0 ? n : null;

    private static bool? Bool(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) ? b : null,
            _ => null,
        } : null;

    /// <summary><c>commits</c> como lista de SHAs, lista de <c>{sha,title}</c> ou texto separado por vírgula/espaço.</summary>
    private static List<(string Sha, string? Title)> Commits(JsonElement o)
    {
        if (!o.TryGetProperty("commits", out var v)) return [];
        if (v.ValueKind == JsonValueKind.Array)
            return v.EnumerateArray().Select(c => c.ValueKind switch
                {
                    JsonValueKind.String => (c.GetString() ?? string.Empty, (string?)null),
                    JsonValueKind.Object => (Str(c, "sha") ?? string.Empty, Str(c, "title") ?? Str(c, "message")),
                    _ => (string.Empty, null),
                })
                .Where(c => c.Item1.Length > 0).Take(250).ToList();
        if (v.ValueKind == JsonValueKind.String)
            return (v.GetString() ?? string.Empty).Split([',', ' ', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(250).Select(s => (s, (string?)null)).ToList();
        return [];
    }

    private static JsonElement ToElement(JsonObject node) => JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
}
