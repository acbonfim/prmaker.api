using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0091: algo que um item de trabalho (ou uma sub-tarefa dele) gerou de fato — PR aberto, branch, código alterado (com a
/// fotografia do diff), chamado, movimento do card, Timeline, horas... Gravado pelo servidor nos pontos onde a coisa
/// acontece (abrir PR, ações do DevOps, Timeline) ou pela skill (<c>deliver-code</c>). Mesma chave na mesma sub-tarefa
/// atualiza. <see cref="WorkItemId"/> null = gerado no card sem sub-tarefa em andamento.
/// </summary>
public class WorkItemDeliverable
{
    public const int MaxTitleLength = 300;
    public const int MaxUrlLength = 1000;
    public const int MaxTextLength = 600;
    public const int MaxKeyLength = 400;
    public const int MaxRefLength = 300;
    public const int MaxDataLength = 200_000;
    /// <summary>Diff guardado por entrega de código; acima disso fica cortado (o resto, pelo compare do GitHub).</summary>
    public const int MaxPatchBytes = 2 * 1024 * 1024;

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public Guid Id { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public Guid? WorkItemId { get; private set; }
    public long? TaskNumber { get; private set; }
    public string Kind { get; private set; } = WorkDeliverableKind.Doc;
    /// <summary>Identidade dentro da sub-tarefa (tipo + repositório + branch/número/url).</summary>
    public string Key { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Url { get; private set; }
    public string? Status { get; private set; }
    public string? Repository { get; private set; }
    public string? Branch { get; private set; }
    public string? Target { get; private set; }
    public int? Number { get; private set; }
    public string? Sha { get; private set; }
    public string? Text { get; private set; }
    /// <summary>JSON (jsonb) com os extras por tipo (commits, arquivos, checks, de → para...).</summary>
    public string DataJson { get; private set; } = "{}";
    /// <summary>auto (servidor) | task (skill/sub-tarefa).</summary>
    public string Source { get; private set; } = WorkDeliverableSource.Auto;
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public WorkItemDeliverableContent? Content { get; private set; }

    protected WorkItemDeliverable() { }

    public WorkItemDeliverable(string cardNumber, Guid? workItemId, long? taskNumber, WorkDeliverableInput input, string source,
        string createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        Id = Guid.NewGuid();
        CardNumber = (cardNumber ?? string.Empty).Trim();
        WorkItemId = workItemId;
        TaskNumber = taskNumber is > 0 ? taskNumber : null;
        Kind = WorkDeliverableKind.Normalize(input.Kind);
        Key = KeyOf(Kind, input);
        Source = source == WorkDeliverableSource.Task ? WorkDeliverableSource.Task : WorkDeliverableSource.Auto;
        CreatedBy = Cut(string.IsNullOrWhiteSpace(createdBy) ? "Claude" : createdBy.Trim(), 200)!;
        CreatedAt = now;
        Apply(input, now);
    }

    public JsonElement DataElement
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DataJson)) return EmptyObject;
            try { return JsonDocument.Parse(DataJson).RootElement.Clone(); }
            catch (JsonException) { return EmptyObject; }
        }
    }

    /// <summary>Tem fotografia do diff (<see cref="Content"/>) — para listar sem ler o conteúdo.</summary>
    public bool HasPatch { get; private set; }
    public bool PatchTruncated { get; private set; }

    /// <summary>Atualiza com uma nova gravação da mesma chave (campos vazios mantêm o valor).</summary>
    public void Apply(WorkDeliverableInput input, DateTimeOffset now)
    {
        Title = Cut(Clean(input.Title), MaxTitleLength) ?? (Title.Length > 0 ? Title : DefaultTitle(Kind, input));
        Url = Cut(Clean(input.Url), MaxUrlLength) ?? Url;
        Status = Cut(Clean(input.Status)?.ToLowerInvariant(), 40) ?? Status;
        Repository = Cut(Clean(input.Repository), MaxRefLength) ?? Repository;
        Branch = Cut(Clean(input.Branch), MaxRefLength) ?? Branch;
        Target = Cut(Clean(input.Target), MaxRefLength) ?? Target;
        Number = input.Number is > 0 ? input.Number : Number;
        Sha = Cut(Clean(input.Sha), 64) ?? Sha;
        Text = Cut(Clean(input.Text), MaxTextLength) ?? Text;
        if (input.Data is { ValueKind: JsonValueKind.Object } data)
        {
            var json = data.GetRawText();
            if (json.Length > MaxDataLength)
                throw new DomainException($"Os dados da entrega passam do limite de {MaxDataLength / 1000} mil caracteres.");
            DataJson = json;
        }
        UpdatedAt = now;
    }

    /// <summary>Grava a fotografia do diff (código). Maior que <see cref="MaxPatchBytes"/> → cortado.</summary>
    public bool SetPatch(byte[] patch)
    {
        var truncated = patch.Length > MaxPatchBytes;
        var data = truncated ? CutPatch(patch) : patch;
        if (Content is null)
            Content = new WorkItemDeliverableContent(Id, data);
        else
            Content.Replace(data);
        HasPatch = true;
        PatchTruncated = truncated;
        return truncated;
    }

    /// <summary>Corta no último fim de linha antes do limite (não deixa meia linha de diff).</summary>
    private static byte[] CutPatch(byte[] patch)
    {
        var end = Array.LastIndexOf(patch, (byte)'\n', MaxPatchBytes - 1);
        return patch[..(end > 0 ? end + 1 : MaxPatchBytes)];
    }

    public static string KeyOf(string kind, WorkDeliverableInput input)
    {
        string? Lower(string? v) => Clean(v)?.ToLowerInvariant();
        var repo = Lower(input.Repository) ?? string.Empty;
        var key = kind switch
        {
            WorkDeliverableKind.PullRequest when input.Number is > 0 && repo.Length > 0 => $"pr:{repo}#{input.Number}",
            WorkDeliverableKind.PullRequest when Lower(input.Url) is { } u => $"pr:{u}",
            WorkDeliverableKind.Branch or WorkDeliverableKind.Code when Lower(input.Branch) is { } b => $"{kind}:{repo}:{b}",
            WorkDeliverableKind.Commit when Lower(input.Sha) is { } s => $"commit:{repo}:{s}",
            WorkDeliverableKind.Ticket or WorkDeliverableKind.Doc or WorkDeliverableKind.PrText when Lower(input.Url) is { } u => $"{kind}:{u}",
            // Movimentos, Timeline, horas, comentários e avisos: cada gravação é uma entrega.
            _ => $"{kind}:{Guid.NewGuid():N}",
        };
        return key.Length <= MaxKeyLength ? key : $"{kind}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))}";
    }

    private static string DefaultTitle(string kind, WorkDeliverableInput i) => Cut(kind switch
    {
        WorkDeliverableKind.PullRequest => $"PR{(i.Number is > 0 ? $" #{i.Number}" : string.Empty)}{(Clean(i.Repository) is { } r ? $" {r}" : string.Empty)}" +
                                           (Clean(i.Branch) is { } b ? $": {b}{(Clean(i.Target) is { } t ? $" → {t}" : string.Empty)}" : string.Empty),
        WorkDeliverableKind.Branch => $"Branch {Clean(i.Branch) ?? "?"}{(Clean(i.Repository) is { } r ? $" ({r})" : string.Empty)}",
        WorkDeliverableKind.Code => $"Código alterado{(Clean(i.Repository) is { } r ? $" em {r}" : string.Empty)}{(Clean(i.Branch) is { } b ? $" ({b})" : string.Empty)}",
        WorkDeliverableKind.Commit => $"Commit {(Clean(i.Sha) is { } s ? s[..Math.Min(7, s.Length)] : "?")}",
        WorkDeliverableKind.Ticket => "Chamado",
        WorkDeliverableKind.PrText => "Texto do PR",
        WorkDeliverableKind.DevOpsState => "Card no DevOps",
        WorkDeliverableKind.Comment => "Comentário no card",
        WorkDeliverableKind.Timeline => "Entrada na Timeline",
        WorkDeliverableKind.Hours => "Horas lançadas",
        WorkDeliverableKind.Notify => "Aviso",
        _ => Clean(i.Url) ?? "Link",
    }, MaxTitleLength)!;

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static string? Cut(string? v, int max) => v is null ? null : v.Length <= max ? v : v[..max];
}

public class WorkItemDeliverableContent
{
    public Guid DeliverableId { get; private set; }
    public byte[] Data { get; private set; } = [];

    protected WorkItemDeliverableContent() { }

    public WorkItemDeliverableContent(Guid deliverableId, byte[] data)
    {
        DeliverableId = deliverableId;
        Data = data;
    }

    public void Replace(byte[] data) => Data = data;
}

/// <summary>0091: o que veio na gravação de uma entrega (tela, skill, MCP ou o próprio servidor).</summary>
public class WorkDeliverableInput
{
    public string? Kind { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? Status { get; set; }
    public string? Repository { get; set; }
    public string? Branch { get; set; }
    public string? Target { get; set; }
    public int? Number { get; set; }
    public string? Sha { get; set; }
    public string? Text { get; set; }
    /// <summary>Extras por tipo (objeto JSON).</summary>
    public JsonElement? Data { get; set; }
}

public static class WorkDeliverableKind
{
    public const string PullRequest = "pr";
    public const string Branch = "branch";
    public const string Commit = "commit";
    public const string Code = "code";
    public const string Ticket = "ticket";
    public const string PrText = "pr-text";
    public const string DevOpsState = "devops-state";
    public const string Comment = "comment";
    public const string Timeline = "timeline";
    public const string Hours = "hours";
    public const string Notify = "notify";
    public const string Doc = "doc";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        PullRequest, Branch, Commit, Code, Ticket, PrText, DevOpsState, Comment, Timeline, Hours, Notify, Doc,
    };

    public static string Normalize(string? kind)
    {
        var k = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return k switch
        {
            "pull-request" or "pullrequest" => PullRequest,
            "devops" or "devops-action" or "state" => DevOpsState,
            "chamado" => Ticket,
            "link" or "other" or "attachment" => Doc,
            _ => All.Contains(k) ? k : throw new DomainException(
                $"Tipo de entrega inválido: '{kind}' (use {string.Join(", ", All.Order(StringComparer.Ordinal))})."),
        };
    }

    /// <summary>Tipo de sub-tarefa que costuma gerar a entrega (para achar a sub-tarefa em andamento quando há várias).</summary>
    public static bool Fits(string deliverableKind, string taskKind) => deliverableKind switch
    {
        PullRequest => taskKind is WorkTaskKind.PullRequest,
        Branch or Commit or Code => taskKind is WorkTaskKind.Code or WorkTaskKind.Branch or WorkTaskKind.CherryPick,
        DevOpsState => taskKind is WorkTaskKind.DevOpsState or WorkTaskKind.DevOpsAction,
        Comment => taskKind is WorkTaskKind.Comment or WorkTaskKind.Notify,
        Ticket => taskKind is WorkTaskKind.Ticket or WorkTaskKind.SqlScript,
        PrText => taskKind is WorkTaskKind.PrText or WorkTaskKind.PullRequest,
        _ => false,
    };
}

public static class WorkDeliverableSource
{
    /// <summary>Gravada pelo servidor onde a coisa aconteceu (abrir PR, DevOps, Timeline).</summary>
    public const string Auto = "auto";
    /// <summary>Gravada pela skill/sub-tarefa (diff do código, output, links).</summary>
    public const string Task = "task";
    /// <summary>Vinda da etapa espelho do plano (links do plano).</summary>
    public const string Plan = "plan";
    /// <summary>Achada nos registros do card (itens anteriores à 0091).</summary>
    public const string Deduced = "deduced";
}
