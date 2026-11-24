using System.Text.Json;

namespace solvace.executionplans.domain.Entities;

/// <summary>0099 (ajustes 2): tipo da história do épico (gravado como texto). Compartilhado com o front — mantenha em sincronia.</summary>
public static class EpicStoryType
{
    public const string UserStory = "user-story";
    public const string Bug = "bug";

    public static readonly IReadOnlyList<string> All = [UserStory, Bug];

    /// <summary><c>user-story</c> | <c>bug</c>; aceita os nomes do DevOps ("User Story", "Bug") e apelidos (us, story, história). Vazio = user-story.</summary>
    public static string Normalize(string? type)
    {
        var t = (type ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');
        return t switch
        {
            "" or "user-story" or "user story" or "userstory" or "us" or "story" or "história" or "historia" => UserStory,
            "bug" or "defeito" => Bug,
            _ => throw new DomainException($"Tipo de história inválido: '{type}' (use {string.Join(" ou ", All)}).")
        };
    }
}

/// <summary>
/// 0099 (ajustes 2): situação da história do épico. <c>draft</c> (rascunho) e <c>ready</c> (pronta para virar card) se editam;
/// <c>converted</c> só pela conversão (vira card do DevOps e não se edita mais); <c>discarded</c> = descartada (DELETE).
/// </summary>
public static class EpicStoryStatus
{
    public const string Draft = "draft";
    public const string Ready = "ready";
    public const string Converted = "converted";
    public const string Discarded = "discarded";

    public static readonly IReadOnlyList<string> All = [Draft, Ready, Converted, Discarded];

    /// <summary>Situação que a pessoa escolhe (rascunho, pronta, descartada) — <c>converted</c> só pela conversão.</summary>
    public static string NormalizeEditable(string? status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (s == Converted)
            throw new DomainException("A história vira 'converted' só pela conversão em card (Converter em card).");
        if (s is not (Draft or Ready or Discarded))
            throw new DomainException($"Situação de história inválida: '{status}' (use draft, ready ou discarded).");
        return s;
    }

    /// <summary>Filtro de lista: um ou vários (vírgula); vazio = todas menos descartadas; <c>all</c> = todas.</summary>
    public static IReadOnlyCollection<string>? ParseFilter(string? status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (s.Length == 0) return [Draft, Ready, Converted];
        if (s is "all" or "todas" or "todos") return null;
        return s.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => All.Contains(v) ? v : throw new DomainException($"Situação de história inválida: '{v}'."))
            .Distinct().ToList();
    }
}

/// <summary>
/// 0099 (ajustes 2): recusa da história que a tela trata pelo <see cref="Code"/> (409): <c>alreadyConverted</c> (já virou o card
/// <see cref="CardId"/>), <c>notEditable</c> (convertida não se edita/descarta) e <c>converting</c> (outra conversão em andamento).
/// </summary>
public class EpicStoryConflictException(string code, string message, string? cardId = null) : Exception(message)
{
    public const string AlreadyConverted = "alreadyConverted";
    public const string NotEditable = "notEditable";
    public const string Converting = "converting";

    public string Code { get; } = code;
    public string? CardId { get; } = cardId;
}

/// <summary>
/// 0099 (ajustes 2): história (User Story) ou bug escrito no PRMake dentro de um épico da Mesa de gestão — rascunho que vira card
/// do DevOps uma vez só (<see cref="BeginConversion"/> → <see cref="CompleteConversion"/>). O <see cref="Number"/> vem da
/// sequência global <c>execution.work_item_number_seq</c> (a mesma de épicos, itens e sub-tarefas): <c>#n</c> resolve a história.
/// Texto em markdown (vira HTML na conversão). <see cref="Fields"/> = campos extras do DevOps <c>{referenceName: valor}</c>
/// (área, iteração, responsável…).
/// </summary>
public class EpicStory
{
    public const int MaxTitleLength = 300;
    public const int MaxTextLength = 100_000;
    public const int MaxSeverityLength = 100;
    public const int MaxFields = 50;
    public const int MaxFieldRefLength = 200;
    public const int MaxFieldValueLength = 10_000;
    public const int MinPriority = 1;
    public const int MaxPriority = 99;
    public const int MaxCardLength = 100;

    /// <summary>Conversão "presa" (o servidor caiu no meio) deixa de travar depois disso.</summary>
    public static readonly TimeSpan ConversionLock = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }
    /// <summary>Número da sequência global (atribuído no repositório antes do insert).</summary>
    public long Number { get; private set; }
    public Guid EpicId { get; private set; }
    /// <summary>Número do épico (foto — o número do épico não muda); evita o join nas leituras.</summary>
    public long EpicNumber { get; private set; }
    public string Type { get; private set; } = EpicStoryType.UserStory;
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? AcceptanceCriteria { get; private set; }
    public string? ReproSteps { get; private set; }
    public int? Priority { get; private set; }
    public string? Severity { get; private set; }
    /// <summary>JSON (jsonb) <c>string[]</c>.</summary>
    public string TagsJson { get; private set; } = "[]";
    /// <summary>JSON (jsonb) <c>{referenceName: valor}</c>.</summary>
    public string FieldsJson { get; private set; } = "{}";
    public string Status { get; private set; } = EpicStoryStatus.Draft;
    /// <summary>Card do DevOps criado pela conversão.</summary>
    public string? CardId { get; private set; }
    public DateTimeOffset? ConvertedAt { get; private set; }
    public string? ConvertedByName { get; private set; }
    /// <summary>Conversão em andamento desde (trava contra duas conversões ao mesmo tempo; null = livre).</summary>
    public DateTimeOffset? ConvertingAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public string CreatedByName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>xmin.</summary>
    public uint Version { get; private set; }

    protected EpicStory() { }

    public EpicStory(Guid epicId, long epicNumber, string? type, string? title, string? description, string? acceptanceCriteria, string? reproSteps,
        int? priority, string? severity, IEnumerable<string>? tags, IReadOnlyDictionary<string, JsonElement>? fields, string? status,
        Guid? createdBy, string? createdByName, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        EpicId = epicId;
        EpicNumber = epicNumber;
        CreatedBy = createdBy;
        CreatedByName = WorkItem.Clean(createdByName, 200) ?? "Usuário";
        CreatedAt = now;
        UpdatedAt = now;
        Type = EpicStoryType.Normalize(type);
        Apply(title, description, acceptanceCriteria, reproSteps, priority, severity, tags, fields);
        if (!string.IsNullOrWhiteSpace(status)) Status = EpicStoryStatus.NormalizeEditable(status);
    }

    public IReadOnlyList<string> Tags
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TagsJson)) return [];
            try { return JsonSerializer.Deserialize<List<string>>(TagsJson, JsonOptions) ?? []; }
            catch (JsonException) { return []; }
        }
    }

    /// <summary>Campos extras do DevOps (sempre objeto).</summary>
    public IReadOnlyDictionary<string, JsonElement> Fields
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FieldsJson)) return new Dictionary<string, JsonElement>();
            try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(FieldsJson, JsonOptions) ?? new Dictionary<string, JsonElement>(); }
            catch (JsonException) { return new Dictionary<string, JsonElement>(); }
        }
    }

    public bool IsConverted => Status == EpicStoryStatus.Converted;

    /// <summary>Conversão em andamento (trava ainda válida em <paramref name="now"/>).</summary>
    public bool IsConverting(DateTimeOffset now) => ConvertingAt is { } at && now - at < ConversionLock;

    // ---------------------------------------------------------------- edição

    /// <summary>
    /// Edita a história (formulário inteiro: campos null limpam; <paramref name="type"/>/<paramref name="status"/> null = não mexe).
    /// Convertida → <see cref="EpicStoryConflictException"/> <c>notEditable</c>.
    /// </summary>
    public void Update(string? type, string? title, string? description, string? acceptanceCriteria, string? reproSteps, int? priority,
        string? severity, IEnumerable<string>? tags, IReadOnlyDictionary<string, JsonElement>? fields, string? status, DateTimeOffset now)
    {
        EnsureEditable(now);
        if (type is not null) Type = EpicStoryType.Normalize(type);
        Apply(title, description, acceptanceCriteria, reproSteps, priority, severity, tags, fields);
        if (!string.IsNullOrWhiteSpace(status)) Status = EpicStoryStatus.NormalizeEditable(status);
        UpdatedAt = now;
    }

    /// <summary>Descarta (DELETE). Idempotente; convertida → <c>notEditable</c>. Devolve se mudou.</summary>
    public bool Discard(DateTimeOffset now)
    {
        EnsureEditable(now);
        if (Status == EpicStoryStatus.Discarded) return false;
        Status = EpicStoryStatus.Discarded;
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Trava a história para a conversão em card: já convertida → <c>alreadyConverted</c> (com o card); outra conversão em andamento
    /// → <c>converting</c>; descartada → 400. Quem chama salva (xmin) ANTES de criar o card — duas conversões ao mesmo tempo: só uma
    /// grava a trava, a outra cai no conflito de concorrência.
    /// </summary>
    public void BeginConversion(DateTimeOffset now)
    {
        if (IsConverted)
            throw new EpicStoryConflictException(EpicStoryConflictException.AlreadyConverted,
                $"A história #{Number} já virou o card {CardId}.", CardId);
        if (IsConverting(now))
            throw new EpicStoryConflictException(EpicStoryConflictException.Converting,
                $"A história #{Number} já está sendo convertida em card — aguarde.");
        if (Status == EpicStoryStatus.Discarded)
            throw new DomainException($"A história #{Number} foi descartada — volte para rascunho antes de converter.");
        if (string.IsNullOrWhiteSpace(Title))
            throw new DomainException("A história precisa de título para virar card.");
        ConvertingAt = now;
    }

    /// <summary>Card criado: marca convertida (não se edita mais) e solta a trava.</summary>
    public void CompleteConversion(string? cardId, string? convertedByName, DateTimeOffset now)
    {
        var card = (cardId ?? string.Empty).Trim().TrimStart('#');
        if (card.Length is 0 or > MaxCardLength || !card.All(char.IsAsciiDigit))
            throw new DomainException($"Card do DevOps inválido na conversão: '{cardId}'.");
        if (IsConverted && CardId != card)
            throw new EpicStoryConflictException(EpicStoryConflictException.AlreadyConverted,
                $"A história #{Number} já virou o card {CardId}.", CardId);
        Status = EpicStoryStatus.Converted;
        CardId = card;
        ConvertedAt = now;
        ConvertedByName = WorkItem.Clean(convertedByName, 200) ?? "Usuário";
        ConvertingAt = null;
        UpdatedAt = now;
    }

    /// <summary>A conversão falhou (DevOps recusou): solta a trava, nada mais muda.</summary>
    public void AbortConversion() => ConvertingAt = null;

    /// <summary>Número atribuído pelo repositório (sequência global) antes do insert.</summary>
    public void AssignNumber(long number)
    {
        if (Number > 0) throw new InvalidOperationException("A história já tem número.");
        if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number));
        Number = number;
    }

    private void EnsureEditable(DateTimeOffset now)
    {
        if (IsConverted)
            throw new EpicStoryConflictException(EpicStoryConflictException.NotEditable,
                $"A história #{Number} já virou o card {CardId} — edite o card no DevOps.", CardId);
        if (IsConverting(now))
            throw new EpicStoryConflictException(EpicStoryConflictException.Converting,
                $"A história #{Number} está sendo convertida em card — aguarde.");
    }

    private void Apply(string? title, string? description, string? acceptanceCriteria, string? reproSteps, int? priority, string? severity,
        IEnumerable<string>? tags, IReadOnlyDictionary<string, JsonElement>? fields)
    {
        var t = (title ?? string.Empty).Trim();
        if (t.Length == 0)
            throw new DomainException("O título da história é obrigatório.");
        Title = t.Length <= MaxTitleLength ? t : t[..MaxTitleLength];
        Description = Text(description, "descrição");
        AcceptanceCriteria = Text(acceptanceCriteria, "critérios de aceite");
        ReproSteps = Text(reproSteps, "passos de reprodução");
        if (priority is { } p && (p < MinPriority || p > MaxPriority))
            throw new DomainException($"Prioridade inválida: {p} (use {MinPriority} a {MaxPriority}; no DevOps, 1 = mais alta).");
        Priority = priority;
        Severity = WorkItem.Clean(severity, MaxSeverityLength);
        TagsJson = JsonSerializer.Serialize(Epic.NormalizeTags(tags), JsonOptions);
        FieldsJson = JsonSerializer.Serialize(NormalizeFields(fields), JsonOptions);
    }

    private static string? Text(string? value, string what)
    {
        var v = value?.Trim();
        if (v is { Length: > MaxTextLength })
            throw new DomainException($"O texto de {what} passa de {MaxTextLength} caracteres.");
        return string.IsNullOrEmpty(v) ? null : v;
    }

    /// <summary>
    /// Campos extras limpos: referência aparada (sem vazias, sem repetir — sem caixa, fica a última), valor texto/número/booleano
    /// (null/vazio sai; texto até <see cref="MaxFieldValueLength"/>), até <see cref="MaxFields"/>.
    /// </summary>
    public static Dictionary<string, JsonElement> NormalizeFields(IReadOnlyDictionary<string, JsonElement>? fields)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawKey, value) in fields ?? new Dictionary<string, JsonElement>())
        {
            var key = (rawKey ?? string.Empty).Trim();
            if (key.Length == 0) continue;
            if (key.Length > MaxFieldRefLength)
                throw new DomainException($"Campo do DevOps com nome longo demais: '{key[..40]}…'.");
            switch (value.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    continue;
                case JsonValueKind.String:
                    var s = value.GetString()?.Trim() ?? string.Empty;
                    if (s.Length == 0) continue;
                    if (s.Length > MaxFieldValueLength)
                        throw new DomainException($"O valor do campo {key} passa de {MaxFieldValueLength} caracteres.");
                    result[key] = JsonSerializer.SerializeToElement(s);
                    break;
                case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
                    result[key] = value.Clone();
                    break;
                default:
                    throw new DomainException($"O campo {key} aceita texto, número ou sim/não.");
            }
            if (result.Count > MaxFields)
                throw new DomainException($"A história aceita até {MaxFields} campos extras.");
        }
        return result;
    }
}
