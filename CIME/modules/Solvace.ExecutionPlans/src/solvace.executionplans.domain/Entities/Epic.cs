using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0099: chave de dono do épico — <c>E{número}</c> (ex.: <c>E1234</c>). Vai como <c>CardNumber</c> em tudo que é "por card"
/// (itens de trabalho, conversa do especialista, arquivos, relatados, pedidos da fila, grupo <c>execplan:E1234</c>), então
/// os componentes da Bancada funcionam sem mudança; os pontos que assumem card do DevOps reconhecem a chave por aqui.
/// </summary>
public static partial class EpicKey
{
    public const string Prefix = "E";

    /// <summary><c>E{número}</c>.</summary>
    public static string Of(long number) => Prefix + number.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Lê a chave (<c>E123</c>, <c>e123</c>, <c>#E123</c>, com espaços nas pontas) e devolve o número do épico. Número de
    /// card do DevOps (só dígitos) ou qualquer outra coisa → false.
    /// </summary>
    public static bool TryParse(string? key, out long number)
    {
        number = 0;
        var k = (key ?? string.Empty).Trim().TrimStart('#').Trim();
        if (!KeyPattern().IsMatch(k)) return false;
        return long.TryParse(k.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
    }

    /// <summary>A chave é de épico (não de card do DevOps)?</summary>
    public static bool IsEpic(string? key) => TryParse(key, out _);

    /// <summary>Chave normalizada (<c>e12</c> → <c>E12</c>); null se não for chave de épico.</summary>
    public static string? Normalize(string? key) => TryParse(key, out var n) ? Of(n) : null;

    [GeneratedRegex("^[Ee][0-9]{1,18}$")]
    private static partial Regex KeyPattern();
}

/// <summary>0099: status do épico (gravado como texto). Compartilhado com o frontend (Mesa de gestão) — mantenha em sincronia.</summary>
public static class EpicStatus
{
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Done = "done";
    public const string Archived = "archived";

    public static readonly IReadOnlyList<string> All = [Active, Paused, Done, Archived];

    /// <summary>Épico fechado (concluído ou arquivado) — guarda <see cref="Epic.ClosedAt"/>.</summary>
    public static bool IsClosed(string status) => status is Done or Archived;

    public static string Normalize(string? status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (!All.Contains(s))
            throw new DomainException($"Status de épico inválido: '{status}' (use {string.Join(", ", All)}).");
        return s;
    }
}

/// <summary>
/// 0099: como o card entrou no épico. <c>created</c> = criado pela Mesa dentro do épico; <c>linked</c> = adicionado à mão;
/// <c>mentioned</c> = citado por <c>#&lt;épico&gt;</c> numa conversa/Timeline/nota do card. Papel só sobe
/// (mentioned → linked → created), nunca é rebaixado.
/// </summary>
public static class EpicCardRole
{
    public const string Created = "created";
    public const string Linked = "linked";
    public const string Mentioned = "mentioned";

    public static readonly IReadOnlyList<string> All = [Created, Linked, Mentioned];

    public static string Normalize(string? role)
    {
        var r = (role ?? string.Empty).Trim().ToLowerInvariant();
        if (r.Length == 0) return Linked;
        if (!All.Contains(r))
            throw new DomainException($"Papel do card no épico inválido: '{role}' (use {string.Join(", ", All)}).");
        return r;
    }

    /// <summary>Peso do papel — o maior vence (mentioned &lt; linked &lt; created).</summary>
    public static int Rank(string role) => role switch
    {
        Created => 2,
        Linked => 1,
        _ => 0
    };
}

/// <summary>
/// 0099 (ajustes 1): filtros do board salvos no épico — a tela carrega e grava (debounce) e o especialista usa no board do time
/// (<c>prmake_devops_board</c>). JSON camelCase <c>{lanes, types, states, assignedTo, tags, text}</c>; listas nunca null (linha
/// antiga sem a chave vira lista vazia). Lane padrão do board = <c>""</c> ("(padrão)"/"default" viram <c>""</c>); responsável pelo
/// <c>uniqueName</c>. <c>hiddenColumns</c>: nomes das colunas do board que o épico não mostra (vazio = todas; coluna nova do DevOps
/// aparece sozinha).
/// </summary>
public class EpicBoardFilters
{
    public const int MaxValues = 50;
    public const int MaxValueLength = 300;
    public const int MaxTextLength = 300;

    private List<string>? _lanes, _hiddenColumns, _types, _states, _assignedTo, _tags;

    public List<string> Lanes { get => _lanes ??= []; set => _lanes = value; }
    /// <summary>Colunas ocultas (nomes) — cards nelas ficam fora do board do épico.</summary>
    public List<string> HiddenColumns { get => _hiddenColumns ??= []; set => _hiddenColumns = value; }
    public List<string> Types { get => _types ??= []; set => _types = value; }
    public List<string> States { get => _states ??= []; set => _states = value; }
    /// <summary><c>uniqueName</c> das pessoas (<c>__unassigned__</c> = sem responsável).</summary>
    public List<string> AssignedTo { get => _assignedTo ??= []; set => _assignedTo = value; }
    public List<string> Tags { get => _tags ??= []; set => _tags = value; }
    public string? Text { get; set; }

    /// <summary>Nenhum filtro ligado.</summary>
    public bool HasAny() => Lanes.Count > 0 || HiddenColumns.Count > 0 || Types.Count > 0 || States.Count > 0 || AssignedTo.Count > 0 || Tags.Count > 0
                            || !string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// Cópia limpa: valores aparados, sem vazios nem repetidos (sem caixa), até <see cref="MaxValues"/> por lista; nas lanes o vazio
    /// é a lane padrão (mantido uma vez) e os apelidos dela viram <c>""</c>; texto aparado (null vira <c>""</c>).
    /// </summary>
    public static EpicBoardFilters Normalize(EpicBoardFilters? filters)
    {
        static List<string> Clean(IEnumerable<string?>? values, bool keepEmpty)
        {
            var result = new List<string>();
            foreach (var raw in values ?? [])
            {
                var v = (raw ?? string.Empty).Trim();
                if (keepEmpty && IsDefaultLane(v)) v = string.Empty;
                if (v.Length == 0 && !keepEmpty) continue;
                if (v.Length > MaxValueLength) v = v[..MaxValueLength];
                if (result.Contains(v, StringComparer.OrdinalIgnoreCase)) continue;
                if (result.Count >= MaxValues)
                    throw new DomainException($"Filtro do board com mais de {MaxValues} valores.");
                result.Add(v);
            }
            return result;
        }
        var text = (filters?.Text ?? string.Empty).Trim();
        return new EpicBoardFilters
        {
            Lanes = Clean(filters?.Lanes, true),
            HiddenColumns = Clean(filters?.HiddenColumns, false),
            Types = Clean(filters?.Types, false),
            States = Clean(filters?.States, false),
            AssignedTo = Clean(filters?.AssignedTo, false),
            Tags = Clean(filters?.Tags, false),
            Text = text.Length <= MaxTextLength ? text : text[..MaxTextLength]
        };
    }

    /// <summary>Apelidos da lane padrão (que no DevOps não tem nome): vazio, "(padrão)", "padrão", "padrao", "default", "default lane".</summary>
    public static bool IsDefaultLane(string? lane)
    {
        var l = (lane ?? string.Empty).Trim().Trim('(', ')').Trim().ToLowerInvariant();
        return l is "" or "padrão" or "padrao" or "default" or "default lane" or "lane padrão" or "lane padrao";
    }
}

/// <summary>
/// 0099: épico da Mesa de gestão — tema de trabalho do PRMake (não é o Epic do DevOps) com itens de trabalho próprios (chave
/// <see cref="EpicKey"/>) e os cards do DevOps que ele organiza (<see cref="Cards"/>). O <see cref="Number"/> vem da sequência
/// global <c>execution.work_item_number_seq</c> (a mesma de itens e sub-tarefas): <c>#n</c> identifica épico, item ou
/// sub-tarefa sem ambiguidade.
/// </summary>
public class Epic
{
    public const int MaxTitleLength = 300;
    public const int MaxDescriptionLength = 100_000;
    public const int MaxColorLength = 20;
    public const int MaxTeamLength = 200;
    public const int MaxBoardLength = 200;
    public const int MaxDevOpsCardLength = 100;
    public const int MaxTags = 30;
    public const int MaxTagLength = 60;
    public const int MaxCards = 2_000;
    public const string DefaultColor = "#7a5cff";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }
    /// <summary>Número da sequência global (atribuído no repositório antes do insert).</summary>
    public long Number { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Status { get; private set; } = EpicStatus.Active;
    public string Color { get; private set; } = DefaultColor;
    /// <summary>JSON (jsonb) <c>string[]</c>.</summary>
    public string TagsJson { get; private set; } = "[]";
    /// <summary>Ajustes 1: JSON (jsonb) <see cref="EpicBoardFilters"/> — filtros do board salvos no épico.</summary>
    public string BoardFiltersJson { get; private set; } = "{}";
    public string? DevOpsTeam { get; private set; }
    public string? DevOpsBoard { get; private set; }
    /// <summary>Card do DevOps que representa o épico (Epic/Feature), opcional.</summary>
    public string? DevOpsCardId { get; private set; }
    /// <summary>
    /// 0104: o Especialista do caso de um card do épico (papel <c>created</c>/<c>linked</c>) pode escrever RASCUNHOS de histórias
    /// aqui (nunca converte sem o papel da Mesa). Ligado por padrão; muda a qualquer momento.
    /// </summary>
    public bool AllowCardStoryDrafts { get; private set; } = true;
    public Guid? CreatedBy { get; private set; }
    public string CreatedByName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    /// <summary>xmin.</summary>
    public uint Version { get; private set; }

    public List<EpicCard> Cards { get; private set; } = [];

    protected Epic() { }

    public Epic(string? title, string? description, string? color, IEnumerable<string>? tags, string? devOpsTeam, string? devOpsBoard,
        string? devOpsCardId, Guid? createdBy, string? createdByName, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CreatedBy = createdBy;
        CreatedByName = WorkItem.Clean(createdByName, 200) ?? "Usuário";
        CreatedAt = now;
        UpdatedAt = now;
        Apply(title, description, color, tags, devOpsTeam, devOpsBoard, devOpsCardId);
    }

    /// <summary><c>E{número}</c>.</summary>
    public string Key => EpicKey.Of(Number);

    public IReadOnlyList<string> Tags
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TagsJson)) return [];
            try { return JsonSerializer.Deserialize<List<string>>(TagsJson, JsonOptions) ?? []; }
            catch (JsonException) { return []; }
        }
    }

    /// <summary>Filtros do board salvos (sempre objeto; listas vazias quando nada foi salvo).</summary>
    public EpicBoardFilters BoardFilters
    {
        get
        {
            if (string.IsNullOrWhiteSpace(BoardFiltersJson)) return new EpicBoardFilters { Text = string.Empty };
            try
            {
                var f = JsonSerializer.Deserialize<EpicBoardFilters>(BoardFiltersJson, JsonOptions) ?? new EpicBoardFilters();
                f.Text ??= string.Empty;
                return f;
            }
            catch (JsonException) { return new EpicBoardFilters { Text = string.Empty }; }
        }
    }

    // ---------------------------------------------------------------- edição

    /// <summary>Edita os dados do épico (campos null limpam, como no formulário — título é obrigatório).</summary>
    public void Update(string? title, string? description, string? color, IEnumerable<string>? tags, string? devOpsTeam, string? devOpsBoard,
        string? devOpsCardId, DateTimeOffset now)
    {
        Apply(title, description, color, tags, devOpsTeam, devOpsBoard, devOpsCardId);
        UpdatedAt = now;
    }

    /// <summary>
    /// Ajustes 1: grava os filtros do board (limpos por <see cref="EpicBoardFilters.Normalize"/>). Não mexe em <see cref="UpdatedAt"/>:
    /// é preferência de visualização (a tela grava com debounce) e não deve reordenar a lista de épicos. Devolve se mudou.
    /// </summary>
    public bool SetBoardFilters(EpicBoardFilters? filters)
    {
        var json = JsonSerializer.Serialize(EpicBoardFilters.Normalize(filters), JsonOptions);
        // O jsonb volta do banco reformatado: compara pelo objeto limpo, não pelo texto gravado.
        if (json == JsonSerializer.Serialize(EpicBoardFilters.Normalize(BoardFilters), JsonOptions)) return false;
        BoardFiltersJson = json;
        return true;
    }

    /// <summary>0104: liga/desliga os rascunhos de histórias pelos cards do épico. Devolve se mudou.</summary>
    public bool SetAllowCardStoryDrafts(bool allow, DateTimeOffset now)
    {
        if (AllowCardStoryDrafts == allow) return false;
        AllowCardStoryDrafts = allow;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Muda o status; concluído/arquivado guarda quando fechou, voltar para ativo/pausado limpa. Devolve se mudou.</summary>
    public bool SetStatus(string? status, DateTimeOffset now)
    {
        var s = EpicStatus.Normalize(status);
        if (s == Status) return false;
        Status = s;
        ClosedAt = EpicStatus.IsClosed(s) ? ClosedAt ?? now : null;
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Põe o card no épico ou reafirma o que já está: o papel só sobe (mentioned → linked → created) e a foto (título/tipo)
    /// é atualizada quando vem. Devolve o card e se mudou algo.
    /// </summary>
    public (EpicCard Card, bool Changed) AddCard(string? cardNumber, string? role, string? title, string? workItemType,
        Guid? addedBy, string? addedByName, DateTimeOffset now)
    {
        var card = EpicCard.NormalizeCard(cardNumber);
        var r = EpicCardRole.Normalize(role);
        var existing = Cards.FirstOrDefault(c => c.CardNumber == card);
        if (existing is not null)
        {
            var changed = existing.Touch(r, title, workItemType);
            if (changed) UpdatedAt = now;
            return (existing, changed);
        }
        if (Cards.Count >= MaxCards)
            throw new DomainException($"O épico #{Number} já tem {MaxCards} cards — crie outro épico.");
        var added = new EpicCard(Id, card, r, title, workItemType, addedBy, addedByName, now);
        Cards.Add(added);
        UpdatedAt = now;
        return (added, true);
    }

    /// <summary>Tira o card do épico. Devolve o removido (null = não estava).</summary>
    public EpicCard? RemoveCard(string? cardNumber, DateTimeOffset now)
    {
        var card = EpicCard.NormalizeCard(cardNumber);
        var existing = Cards.FirstOrDefault(c => c.CardNumber == card);
        if (existing is null) return null;
        Cards.Remove(existing);
        UpdatedAt = now;
        return existing;
    }

    /// <summary>Número atribuído pelo repositório (sequência global) antes do insert.</summary>
    public void AssignNumber(long number)
    {
        if (Number > 0) throw new InvalidOperationException("O épico já tem número.");
        if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number));
        Number = number;
    }

    private void Apply(string? title, string? description, string? color, IEnumerable<string>? tags, string? devOpsTeam, string? devOpsBoard,
        string? devOpsCardId)
    {
        var t = (title ?? string.Empty).Trim();
        if (t.Length == 0)
            throw new DomainException("O título do épico é obrigatório.");
        Title = t.Length <= MaxTitleLength ? t : t[..MaxTitleLength];

        var d = description?.Trim();
        if (d is { Length: > MaxDescriptionLength })
            throw new DomainException($"A descrição do épico passa de {MaxDescriptionLength} caracteres.");
        Description = string.IsNullOrEmpty(d) ? null : d;

        Color = NormalizeColor(color);
        TagsJson = JsonSerializer.Serialize(NormalizeTags(tags), JsonOptions);
        DevOpsTeam = WorkItem.Clean(devOpsTeam, MaxTeamLength);
        DevOpsBoard = WorkItem.Clean(devOpsBoard, MaxBoardLength);
        var cardId = WorkItem.Clean(devOpsCardId, MaxDevOpsCardLength)?.TrimStart('#');
        DevOpsCardId = string.IsNullOrWhiteSpace(cardId) ? null : cardId;
    }

    /// <summary>Cor hexa (<c>#rgb</c> ou <c>#rrggbb</c>, minúsculas); vazio = a padrão.</summary>
    public static string NormalizeColor(string? color)
    {
        var c = (color ?? string.Empty).Trim().ToLowerInvariant();
        if (c.Length == 0) return DefaultColor;
        if (!c.StartsWith('#')) c = "#" + c;
        if (c.Length is not (4 or 7) || !c.Skip(1).All(Uri.IsHexDigit))
            throw new DomainException($"Cor inválida: '{color}' (use #rrggbb).");
        return c;
    }

    /// <summary>Tags sem repetir (ignora caixa), sem vazias, até <see cref="MaxTags"/> de até <see cref="MaxTagLength"/>.</summary>
    public static List<string> NormalizeTags(IEnumerable<string>? tags)
    {
        var result = new List<string>();
        foreach (var raw in tags ?? [])
        {
            var tag = (raw ?? string.Empty).Trim().TrimStart('#').Trim();
            if (tag.Length == 0) continue;
            if (tag.Length > MaxTagLength) tag = tag[..MaxTagLength];
            if (result.Contains(tag, StringComparer.OrdinalIgnoreCase)) continue;
            if (result.Count >= MaxTags)
                throw new DomainException($"O épico aceita até {MaxTags} tags.");
            result.Add(tag);
        }
        return result;
    }
}

/// <summary>
/// 0099: card do DevOps dentro de um épico (um card pode estar em vários). Título/tipo são uma foto de quando entrou (a tela
/// lê o estado atual do DevOps). Único por (épico, card).
/// </summary>
public class EpicCard
{
    public const int MaxCardLength = 100;
    public const int MaxTitleLength = 300;
    public const int MaxTypeLength = 100;

    public Guid Id { get; private set; }
    public Guid EpicId { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public string Role { get; private set; } = EpicCardRole.Linked;
    public string? Title { get; private set; }
    public string? WorkItemType { get; private set; }
    public Guid? AddedBy { get; private set; }
    public string AddedByName { get; private set; } = string.Empty;
    public DateTimeOffset AddedAt { get; private set; }

    protected EpicCard() { }

    public EpicCard(Guid epicId, string cardNumber, string role, string? title, string? workItemType, Guid? addedBy, string? addedByName,
        DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        EpicId = epicId;
        CardNumber = NormalizeCard(cardNumber);
        Role = EpicCardRole.Normalize(role);
        Title = WorkItem.Clean(title, MaxTitleLength);
        WorkItemType = WorkItem.Clean(workItemType, MaxTypeLength);
        AddedBy = addedBy;
        AddedByName = WorkItem.Clean(addedByName, 200) ?? "Usuário";
        AddedAt = now;
    }

    /// <summary>Papel só sobe; foto atualizada quando vem. Devolve se mudou.</summary>
    public bool Touch(string role, string? title, string? workItemType)
    {
        var changed = false;
        var r = EpicCardRole.Normalize(role);
        if (EpicCardRole.Rank(r) > EpicCardRole.Rank(Role)) { Role = r; changed = true; }
        if (WorkItem.Clean(title, MaxTitleLength) is { } t && t != Title) { Title = t; changed = true; }
        if (WorkItem.Clean(workItemType, MaxTypeLength) is { } w && w != WorkItemType) { WorkItemType = w; changed = true; }
        return changed;
    }

    /// <summary>Número do card do DevOps (<c>123</c>, <c>#123</c>, <c>AB#123</c>) — só dígitos; chave de épico não entra.</summary>
    public static string NormalizeCard(string? card)
    {
        var c = (card ?? string.Empty).Trim();
        if (c.StartsWith("AB#", StringComparison.OrdinalIgnoreCase)) c = c[3..];
        c = c.TrimStart('#').Trim();
        if (EpicKey.IsEpic(c))
            throw new DomainException("Um épico não entra como card de outro épico — informe o número do card do DevOps.");
        if (c.Length is 0 or > MaxCardLength || !c.All(char.IsAsciiDigit))
            throw new DomainException($"Card do DevOps inválido: '{card}' (informe o número do card).");
        return c;
    }
}
