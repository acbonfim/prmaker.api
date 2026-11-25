using System.Text.Json;
using solvace.executionplans.domain.Requests;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0100: status do pedido de coleta da AWS (<see cref="AwsRefresh"/>). Compartilhado com o frontend (aba AWS) e com o
/// executor (laço AWS) — mantenha em sincronia.
/// </summary>
public static class AwsRefreshStatus
{
    public const string Queued = "queued";
    public const string Claimed = "claimed";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Queued, Claimed, Done, Failed, Expired, Cancelled };

    /// <summary>Ainda na fila ou com um executor (um ativo por card + pessoa).</summary>
    public static bool IsActive(string status) => status is Queued or Claimed;
}

/// <summary>0100: de onde veio o pedido de coleta.</summary>
public static class AwsRefreshSource
{
    /// <summary>Botão Atualizar da aba AWS.</summary>
    public const string Button = "button";
    /// <summary>Acompanhamento do deploy: o retrato tinha ambiente em andamento e o servidor reagendou.</summary>
    public const string Follow = "follow";
    /// <summary>Sub-tarefa <c>aws-deploy-map</c> de um item de trabalho.</summary>
    public const string Task = "task";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Button, Follow, Task };
}

/// <summary>0100: veredito por ambiente/pipeline no retrato (texto fixo na tela; o script calcula).</summary>
public static class AwsVerdict
{
    public const string Failed = "failed";
    public const string Deploying = "deploying";
    public const string Waiting = "waiting";
    public const string Absent = "absent";
    public const string Deployed = "deployed";
    public const string NotApplicable = "not-applicable";
    public const string Unknown = "unknown";
    /// <summary>Esteira que já falhava antes do nosso código chegar — não conta contra o card.</summary>
    public const string Broken = "broken";
}

/// <summary>
/// 0100: linha do índice de pipelines da AWS (CodePipeline), compartilhado por todas as máquinas que coletam. O script
/// compara o <c>updated</c> da AWS (texto devolvido como veio) e só busca a definição das novas/alteradas.
/// </summary>
public class AwsPipeline
{
    public const int MaxNameLength = 200;
    /// <summary>Tamanho máximo de uma pipeline serializada (o resto é recusado no envio).</summary>
    public const int MaxDtoBytes = 16 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Nome da pipeline (chave).</summary>
    public string Name { get; private set; } = string.Empty;
    public string? Account { get; private set; }
    public string? Region { get; private set; }
    /// <summary><c>updated</c> da AWS (list-pipelines), guardado como texto exatamente como o script mandou.</summary>
    public string? Updated { get; private set; }
    /// <summary>V1 | V2.</summary>
    public string? Type { get; private set; }
    /// <summary>JSON (jsonb): [{repository, branch, action}].</summary>
    public string RepositoriesJson { get; private set; } = "[]";
    /// <summary>Repositórios em minúsculas entre barras (<c>|electradv/edv-solvace-apps|</c>) — busca por repositório.</summary>
    public string RepositoryKeys { get; private set; } = string.Empty;
    /// <summary>JSON (jsonb): {include: [...], exclude: [...]} do gatilho por push (V2).</summary>
    public string FilePathsJson { get; private set; } = "{}";
    public string? Tier { get; private set; }
    public string? CodebuildEnv { get; private set; }
    /// <summary>JSON (jsonb): [{action, stage, provider, target}].</summary>
    public string DeployTargetsJson { get; private set; } = "[]";
    /// <summary>JSON (jsonb): nomes dos estágios.</summary>
    public string StagesJson { get; private set; } = "[]";
    /// <summary>Quando o índice recebeu esta pipeline pela última vez.</summary>
    public DateTimeOffset SeenAt { get; private set; }

    protected AwsPipeline() { }

    public AwsPipeline(AwsPipelineDto dto, string? account, string? region, DateTimeOffset now)
    {
        Name = NormalizeName(dto?.Name);
        Apply(dto!, account, region, now);
    }

    public static string NormalizeName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0 || n.Length > MaxNameLength)
            throw new DomainException($"Nome de pipeline inválido: '{name}'.");
        return n;
    }

    /// <summary>Tamanho da pipeline serializada (limite <see cref="MaxDtoBytes"/>).</summary>
    public static int SizeOf(AwsPipelineDto dto) => JsonSerializer.SerializeToUtf8Bytes(dto, Json).Length;

    public void Apply(AwsPipelineDto dto, string? account, string? region, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Account = Cut(account, 20) ?? Account;
        Region = Cut(region, 30) ?? Region;
        Updated = Cut(dto.Updated, 64);
        Type = Cut(dto.Type, 10);
        var repos = (dto.Repositories ?? []).Where(r => !string.IsNullOrWhiteSpace(r.Repository)).ToList();
        RepositoriesJson = JsonSerializer.Serialize(repos, Json);
        RepositoryKeys = repos.Count == 0 ? string.Empty
            : "|" + string.Join("|", repos.Select(r => r.Repository!.Trim().ToLowerInvariant()).Distinct()) + "|";
        FilePathsJson = JsonSerializer.Serialize(new FilePaths(dto.FilePathsInclude ?? [], dto.FilePathsExclude ?? []), Json);
        Tier = Cut(dto.Tier, 20);
        CodebuildEnv = Cut(dto.CodebuildEnv, 40);
        DeployTargetsJson = JsonSerializer.Serialize(dto.DeployTargets ?? [], Json);
        StagesJson = JsonSerializer.Serialize(dto.Stages ?? [], Json);
        SeenAt = now;
    }

    /// <summary>Chave de busca de um repositório (<c>|repo|</c>, minúsculas).</summary>
    public static string RepositoryKey(string repository) => "|" + (repository ?? string.Empty).Trim().ToLowerInvariant() + "|";

    public AwsPipelineDto ToDto()
    {
        var paths = Read<FilePaths>(FilePathsJson) ?? new FilePaths([], []);
        return new AwsPipelineDto
        {
            Name = Name,
            Updated = Updated,
            Type = Type,
            Repositories = Read<List<AwsPipelineRepositoryDto>>(RepositoriesJson) ?? [],
            FilePathsInclude = paths.Include ?? [],
            FilePathsExclude = paths.Exclude ?? [],
            Tier = Tier,
            CodebuildEnv = CodebuildEnv,
            DeployTargets = Read<List<AwsDeployTargetDto>>(DeployTargetsJson) ?? [],
            Stages = Read<List<string>>(StagesJson) ?? [],
        };
    }

    private sealed record FilePaths(List<string> Include, List<string> Exclude);

    private static T? Read<T>(string json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Json); }
        catch (JsonException) { return default; }
    }

    private static string? Cut(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= max ? v : v[..max];
    }
}

/// <summary>
/// 0100: o que o envio do índice faz nas linhas que já existem — puro, para testar: as que vieram são gravadas
/// (novas/alteradas), as de <c>removed</c> saem e, com <c>full</c>, sai também tudo o que não veio.
/// </summary>
public static class AwsPipelineIndex
{
    public const int MaxBatch = 1500;

    public sealed record Plan(IReadOnlyList<AwsPipelineDto> Upserts, IReadOnlyList<string> Remove, IReadOnlyList<string> Skipped);

    public static Plan Build(IEnumerable<string> existing, IReadOnlyList<AwsPipelineDto>? upserts, IReadOnlyList<string>? removed, bool full)
    {
        upserts ??= [];
        removed ??= [];
        if (upserts.Count > MaxBatch || removed.Count > MaxBatch * 2)
            throw new DomainException($"Envie no máximo {MaxBatch} pipelines por vez.");

        // A última ocorrência do mesmo nome vale; nome inválido ou pipeline grande demais fica de fora (skipped).
        var byName = new Dictionary<string, AwsPipelineDto>(StringComparer.Ordinal);
        var skipped = new List<string>();
        foreach (var dto in upserts)
        {
            if (dto is null) continue;
            var name = dto.Name?.Trim() ?? string.Empty;
            if (name.Length == 0 || name.Length > AwsPipeline.MaxNameLength || AwsPipeline.SizeOf(dto) > AwsPipeline.MaxDtoBytes)
            {
                skipped.Add(name.Length == 0 ? "(sem nome)" : name);
                continue;
            }
            dto.Name = name;
            byName[name] = dto;
        }

        var current = existing.ToHashSet(StringComparer.Ordinal);
        var remove = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in removed)
        {
            var n = r?.Trim();
            if (!string.IsNullOrEmpty(n) && current.Contains(n) && !byName.ContainsKey(n)) remove.Add(n);
        }
        if (full)
            foreach (var n in current)
                if (!byName.ContainsKey(n)) remove.Add(n);

        return new Plan(byName.Values.ToList(), remove.OrderBy(n => n, StringComparer.Ordinal).ToList(), skipped);
    }
}

/// <summary>
/// 0100: último retrato da AWS de um card (um por card; a coleta nova substitui). O JSON vem do script da máquina
/// (contrato em features/0100/plan.md) e é devolvido como veio.
/// </summary>
public class AwsCardSnapshot
{
    public const int MaxDataBytes = 2 * 1024 * 1024;

    public string CardNumber { get; private set; } = string.Empty;
    /// <summary>JSON (jsonb) do retrato.</summary>
    public string DataJson { get; private set; } = "{}";
    public DateTimeOffset CollectedAt { get; private set; }
    public string CollectedBy { get; private set; } = string.Empty;
    public Guid? CollectedByUserId { get; private set; }
    /// <summary><c>version</c> do formato do retrato.</summary>
    public int Version { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    protected AwsCardSnapshot() { }

    public AwsCardSnapshot(string cardNumber, string json, DateTimeOffset collectedAt, string collectedBy, Guid? userId, int version, DateTimeOffset now)
    {
        CardNumber = AwsRefresh.NormalizeCard(cardNumber);
        Replace(json, collectedAt, collectedBy, userId, version, now);
    }

    public void Replace(string json, DateTimeOffset collectedAt, string collectedBy, Guid? userId, int version, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new DomainException("Retrato vazio.");
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxDataBytes)
            throw new DomainException($"O retrato passa de {MaxDataBytes / 1024 / 1024} MB — corte logs/execuções antes de enviar.");
        DataJson = json;
        CollectedAt = collectedAt;
        var by = (collectedBy ?? string.Empty).Trim();
        CollectedBy = by.Length == 0 ? "Usuário" : by.Length <= 200 ? by : by[..200];
        CollectedByUserId = userId;
        Version = version;
        UpdatedAt = now;
    }
}

/// <summary>
/// 0100: pedido de coleta da AWS para a máquina de quem pediu (regra de segurança 78723: só um executor de
/// <see cref="RequestedByUserId"/> pega — nunca o de outra pessoa). Fila própria (como a promoção 0074): não ocupa a pista
/// do Claude. Trava de 5 min por executor; na fila há mais de 30 min → expira.
/// </summary>
public class AwsRefresh
{
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan QueueTimeout = TimeSpan.FromMinutes(30);
    public const int MaxErrorLength = 1000;

    public Guid Id { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public Guid RequestedByUserId { get; private set; }
    public string RequestedByName { get; private set; } = string.Empty;
    public string Status { get; private set; } = AwsRefreshStatus.Queued;
    public string Source { get; private set; } = AwsRefreshSource.Button;
    public Guid? WorkerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    /// <summary>Acompanhamento: não sai da fila antes disso.</summary>
    public DateTimeOffset? NotBefore { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public string? Error { get; private set; }
    public int Attempt { get; private set; }
    /// <summary>Quantas coletas de acompanhamento já vieram antes desta (0 = pedido original).</summary>
    public int FollowCount { get; private set; }
    public long? DurationMs { get; private set; }
    public uint RowVersion { get; private set; }

    protected AwsRefresh() { }

    public AwsRefresh(string cardNumber, Guid requestedBy, string requestedByName, string source, int followCount, DateTimeOffset? notBefore,
        DateTimeOffset now)
    {
        if (requestedBy == Guid.Empty)
            throw new DomainException("Pedido de coleta sem a pessoa que pediu.");
        var s = (source ?? string.Empty).Trim().ToLowerInvariant();
        if (!AwsRefreshSource.All.Contains(s))
            throw new DomainException($"Origem de coleta inválida: '{source}'.");
        Id = Guid.NewGuid();
        CardNumber = NormalizeCard(cardNumber);
        RequestedByUserId = requestedBy;
        var name = (requestedByName ?? string.Empty).Trim();
        RequestedByName = name.Length == 0 ? "Usuário" : name.Length <= 200 ? name : name[..200];
        Source = s;
        FollowCount = Math.Max(0, followCount);
        NotBefore = notBefore;
        CreatedAt = now;
    }

    public static string NormalizeCard(string? card)
    {
        var c = (card ?? string.Empty).Trim();
        if (c.Length == 0 || c.Length > 100)
            throw new DomainException("Informe o número do card.");
        return c;
    }

    public bool IsActive => AwsRefreshStatus.IsActive(Status);

    /// <summary>Pode sair da fila agora (acompanhamento respeita <see cref="NotBefore"/>).</summary>
    public bool IsReady(DateTimeOffset now) => Status == AwsRefreshStatus.Queued && (NotBefore is null || NotBefore <= now);

    /// <summary>Só o executor de quem pediu pega o pedido (regra 78723).</summary>
    public void Claim(Guid owner, Guid workerId, DateTimeOffset now)
    {
        if (owner != RequestedByUserId)
            throw new DomainException("Este pedido de coleta é de outra pessoa — só o executor de quem pediu pode pegá-lo.");
        if (!IsReady(now))
            throw new DomainException($"O pedido de coleta está {Status} — não dá para pegar.");
        Status = AwsRefreshStatus.Claimed;
        WorkerId = workerId;
        ClaimedAt = now;
        LeaseUntil = now + Lease;
        Attempt++;
    }

    /// <summary>O executor ainda está coletando: estende a trava.</summary>
    public bool Heartbeat(Guid workerId, DateTimeOffset now)
    {
        // O retrato já gravado conclui o pedido antes do script sair: continua sendo deste executor (não interromper).
        if (Status == AwsRefreshStatus.Done && WorkerId == workerId) return true;
        if (Status != AwsRefreshStatus.Claimed || WorkerId != workerId) return false;
        LeaseUntil = now + Lease;
        return true;
    }

    /// <summary>
    /// Vencidos: na fila há mais de 30 min (contando do <see cref="NotBefore"/>) ou com o executor sem resposta depois da
    /// trava → <c>expired</c>. Devolve se mudou.
    /// </summary>
    public bool ExpireIfStale(DateTimeOffset now)
    {
        if (Status == AwsRefreshStatus.Queued && now - (NotBefore ?? CreatedAt) > QueueTimeout)
        {
            Close(AwsRefreshStatus.Expired, "Nenhum executor de quem pediu pegou a coleta em 30 min.", null, now);
            return true;
        }
        if (Status == AwsRefreshStatus.Claimed && LeaseUntil is { } lease && lease < now)
        {
            Close(AwsRefreshStatus.Expired, "O executor não respondeu dentro da trava de 5 min.", null, now);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Fim informado pelo executor (<c>report</c>). Aceita também o pedido que venceu a trava enquanto o mesmo executor
    /// terminava (a coleta chegou, vale o resultado).
    /// </summary>
    public void Report(Guid owner, Guid workerId, string status, string? error, long? durationMs, DateTimeOffset now)
    {
        if (owner != RequestedByUserId)
            throw new DomainException("Este pedido de coleta é de outra pessoa.");
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (s is not (AwsRefreshStatus.Done or AwsRefreshStatus.Failed))
            throw new DomainException($"Resultado inválido: '{status}' (use done ou failed).");
        var mine = WorkerId == workerId;
        if (!(Status == AwsRefreshStatus.Claimed && mine) && !(Status == AwsRefreshStatus.Expired && mine) && !(Status == AwsRefreshStatus.Done && mine))
            throw new DomainException("Este pedido de coleta não está com este executor.");
        if (Status == AwsRefreshStatus.Done)
        {
            // O PUT do retrato já concluiu: o retrato vale, um "failed" que chega depois (ex.: script interrompido no
            // fim) não o desfaz.
            DurationMs = durationMs ?? DurationMs;
            return;
        }
        Close(s, s == AwsRefreshStatus.Failed ? error ?? "A coleta falhou no executor." : null, durationMs, now);
    }

    /// <summary>O retrato com este <c>requestId</c> chegou: o pedido ativo (ou vencido) conclui.</summary>
    public bool CompleteBySnapshot(long? durationMs, DateTimeOffset now)
    {
        if (!(IsActive || Status == AwsRefreshStatus.Expired)) return false;
        Close(AwsRefreshStatus.Done, null, durationMs, now);
        return true;
    }

    /// <summary>A pessoa pediu de novo com um acompanhamento ainda esperando a hora: sai já.</summary>
    public bool Expedite(DateTimeOffset now)
    {
        if (Status != AwsRefreshStatus.Queued || NotBefore is null || NotBefore <= now) return false;
        NotBefore = null;
        return true;
    }

    public void Cancel(string? reason, DateTimeOffset now)
    {
        if (!IsActive) return;
        Close(AwsRefreshStatus.Cancelled, reason, null, now);
    }

    private void Close(string status, string? error, long? durationMs, DateTimeOffset now)
    {
        Status = status;
        var e = error?.Trim();
        Error = string.IsNullOrEmpty(e) ? null : e.Length <= MaxErrorLength ? e : e[..MaxErrorLength];
        DurationMs = durationMs is >= 0 ? durationMs : DurationMs;
        FinishedAt = now;
        LeaseUntil = null;
    }
}

/// <summary>0100: regras da fila de coleta — puras, para testar.</summary>
public static class AwsRefreshQueue
{
    /// <summary>
    /// Próximo pedido para um executor de <paramref name="owner"/>: só os pedidos DELE (nunca de outra pessoa), prontos,
    /// do mais antigo para o mais novo; um card por vez (card já com coleta em andamento nesta pessoa espera).
    /// </summary>
    public static AwsRefresh? PickNext(IEnumerable<AwsRefresh> candidates, Guid owner, DateTimeOffset now)
    {
        var mine = candidates.Where(r => r.RequestedByUserId == owner).ToList();
        var busy = mine.Where(r => r.Status == AwsRefreshStatus.Claimed).Select(r => r.CardNumber).ToHashSet(StringComparer.Ordinal);
        return mine.Where(r => r.IsReady(now) && !busy.Contains(r.CardNumber))
            .OrderBy(r => r.NotBefore ?? r.CreatedAt).ThenBy(r => r.CreatedAt)
            .FirstOrDefault();
    }

    /// <summary>
    /// Acompanhamento do deploy: retrato com ambiente <c>deploying</c> reagenda a coleta para a mesma pessoa daqui a
    /// <paramref name="followMinutes"/>, até <paramref name="followMax"/> vezes. Null = não reagenda.
    /// </summary>
    /// <param name="finished">Pedido que este retrato concluiu (null = coleta manual/sub-tarefa).</param>
    /// <param name="hasActive">A pessoa já tem pedido ativo para o card (um por vez).</param>
    public static AwsRefresh? Follow(string card, bool deploying, AwsRefresh? finished, Guid? collector, string collectorName, bool hasActive,
        int followMinutes, int followMax, DateTimeOffset now)
    {
        if (!deploying || hasActive || followMax <= 0) return null;
        var owner = finished?.RequestedByUserId ?? collector;
        if (owner is null || owner == Guid.Empty) return null;
        var count = (finished?.FollowCount ?? 0) + 1;
        if (count > followMax) return null;
        var minutes = Math.Clamp(followMinutes, 1, 60);
        return new AwsRefresh(card, owner.Value, finished?.RequestedByName ?? collectorName, AwsRefreshSource.Follow, count,
            now.AddMinutes(minutes), now);
    }
}
