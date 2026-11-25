using System.Security.Claims;
using System.Text;
using System.Text.Json;
using solvace.azure.application.Contract;
using solvace.azure.application.Services;
using solvace.azure.domain.Models;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;
using solvace.prform.Skills;

namespace solvace.prform.Execution;

/// <summary>
/// 0099 (ajustes 2): para onde vai cada parte da história no card do DevOps, por tipo (<c>DeskStoryFieldMap</c> no "Skills
/// Configurations"): <c>{"user-story": {type, description, acceptanceCriteria, priority?, severity?}, "bug": {type, description,
/// reproSteps, ...}}</c> — valores são o <c>referenceName</c> do campo. Tipo ausente na configuração = o padrão do código; texto sem
/// campo (ex.: critérios de aceite num Bug, ou <c>""</c>) vai como seção da descrição; prioridade/severidade sem chave = os campos
/// padrão do DevOps (<c>""</c> desliga).
/// </summary>
public sealed record DeskStoryTypeMap(string Type, string Description, string? AcceptanceCriteria, string? ReproSteps, string? Priority, string? Severity);

public static class DeskStoryFieldMap
{
    /// <summary>O mesmo da migração <c>SeedDeskStoryFieldMap0099</c> (DefaultContext).</summary>
    public const string DefaultJson = """
{
  "user-story": { "type": "User Story", "description": "System.Description", "acceptanceCriteria": "Microsoft.VSTS.Common.AcceptanceCriteria" },
  "bug": { "type": "Bug", "description": "System.Description", "reproSteps": "Microsoft.VSTS.TCM.ReproSteps" }
}
""";

    public const string DefaultDescriptionField = "System.Description";
    public const string DefaultPriorityField = "Microsoft.VSTS.Common.Priority";
    public const string DefaultSeverityField = "Microsoft.VSTS.Common.Severity";

    /// <summary>Mapeamento do tipo da história (<c>user-story</c>|<c>bug</c>) — configuração inválida ou sem o tipo = o padrão.</summary>
    public static DeskStoryTypeMap For(string? json, string storyType)
    {
        var type = EpicStoryType.Normalize(storyType);
        return Parse(json, type) ?? Parse(DefaultJson, type)!;
    }

    private static DeskStoryTypeMap? Parse(string? json, string type)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            JsonElement entry = default;
            var found = false;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                string normalized;
                try { normalized = EpicStoryType.Normalize(p.Name); }
                catch (DomainException) { continue; }
                if (normalized != type || p.Value.ValueKind != JsonValueKind.Object) continue;
                entry = p.Value;
                found = true;
                break;
            }
            if (!found) return null;

            string? Value(string key, string? fallback)
            {
                if (!entry.TryGetProperty(key, out var v)) return fallback;
                return v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() is { Length: > 0 } s ? s : null : fallback;
            }
            var isBug = type == EpicStoryType.Bug;
            return new DeskStoryTypeMap(
                Value("type", null) ?? (isBug ? "Bug" : "User Story"),
                Value("description", null) ?? DefaultDescriptionField,
                Value("acceptanceCriteria", null),
                Value("reproSteps", null),
                Value("priority", DefaultPriorityField),
                Value("severity", isBug ? DefaultSeverityField : null));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Pedido de criação do card a partir da história: título, tipo e tags; descrição/critérios/passos em HTML (markdown → HTML, o mesmo
    /// conversor dos comentários); prioridade/severidade quando não vierem nos campos extras; campos extras da história; o épico (o card
    /// entra nele como <c>created</c>); time/board/coluna/lane do pedido ou o board preferido do épico (board só com o time do épico).
    /// </summary>
    public static DevOpsCreateCardRequest BuildRequest(EpicStoryResponse story, DeskStoryTypeMap map, ConvertEpicStoryRequest? request,
        string? epicTeam, string? epicBoard)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in story.Fields) fields[key] = value;
        foreach (var (key, value) in request?.Fields ?? []) fields[key] = value;

        var description = new StringBuilder(story.Description?.Trim() ?? string.Empty);
        void Section(string title, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (description.Length > 0) description.Append("\n\n");
            description.Append("## ").Append(title).Append("\n\n").Append(text.Trim());
        }

        if (!string.IsNullOrWhiteSpace(story.AcceptanceCriteria))
        {
            if (map.AcceptanceCriteria is { } ac) fields[ac] = Html(story.AcceptanceCriteria);
            else Section("Critérios de aceite", story.AcceptanceCriteria);
        }
        if (!string.IsNullOrWhiteSpace(story.ReproSteps))
        {
            if (map.ReproSteps is { } rs) fields[rs] = Html(story.ReproSteps);
            else Section("Passos para reproduzir", story.ReproSteps);
        }
        if (description.Length > 0) fields[map.Description] = Html(description.ToString());
        if (story.Priority is { } priority && map.Priority is { } pf && !fields.ContainsKey(pf))
            fields[pf] = JsonSerializer.SerializeToElement(priority);
        if (!string.IsNullOrWhiteSpace(story.Severity) && map.Severity is { } sf && !fields.ContainsKey(sf))
            fields[sf] = JsonSerializer.SerializeToElement(story.Severity.Trim());

        var team = Clean(request?.Team) ?? Clean(epicTeam);
        var board = Clean(request?.Board)
                    ?? (team is not null && string.Equals(team, Clean(epicTeam), StringComparison.OrdinalIgnoreCase) ? Clean(epicBoard) : null);
        return new DevOpsCreateCardRequest
        {
            Type = map.Type,
            Title = story.Title,
            Fields = fields.Count > 0 ? fields : null,
            Tags = story.Tags.Count > 0 ? story.Tags.ToList() : null,
            Epic = story.EpicNumber,
            Team = team,
            Board = board,
            Column = Clean(request?.Column),
            Lane = request?.Lane
        };
    }

    private static JsonElement Html(string markdown) => JsonSerializer.SerializeToElement(UserStorySpec.ToHtml(markdown));

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>0099 (ajustes 2): história convertida — a história (agora <c>converted</c>), o card criado e o resumo do DevOps.</summary>
public sealed record EpicStoryConversionResult(EpicStoryResponse Story, DevOpsCardDetail Card, string Summary);

/// <summary>
/// 0099 (ajustes 2): converte a história do épico em card do DevOps — trava a história (uma conversão só), cria o card pelo
/// <see cref="DevOpsDeskRunner"/> (Timeline do card, <c>devopsCardsUpdated</c>, card no épico como <c>created</c>) com a integração de
/// quem age e marca a história como convertida (<c>epicsUpdated</c> action <c>stories</c>). DevOps recusou → solta a trava e repassa o
/// erro (a história continua editável).
/// </summary>
public class EpicStoryConverter(
    IEpicStoryApplication stories,
    IEpicApplication epics,
    DevOpsDeskRunner runner,
    SkillsRulesService rules,
    IDevOpsDeskService devops,
    ILogger<EpicStoryConverter> logger)
{
    /// <summary>
    /// Card 1109 (E1093): campos que o DevOps exige no tipo da história (<c>user-story</c>|<c>bug</c>, pelo <c>DeskStoryFieldMap</c>) e
    /// não preenche sozinho — o formulário da história e o "Converter em card" pedem cada um (ex.: <c>Custom.Module</c>, picklist).
    /// </summary>
    public async Task<(string DevOpsType, IReadOnlyList<DevOpsFieldInfo> Fields)> RequiredFieldsAsync(string storyType, CancellationToken ct)
    {
        var settings = await rules.SettingsOnlyAsync(ct);
        settings.TryGetValue(SkillsConfigurationKeys.DeskStoryFieldMap, out var json);
        var map = DeskStoryFieldMap.For(json, storyType);
        return (map.Type, await devops.MissingRequiredFieldsAsync(map.Type, null, ct));
    }

    public async Task<EpicStoryConversionResult> ConvertAsync(long number, ConvertEpicStoryRequest? request, ExecutionActor actor, ClaimsPrincipal user,
        string? via, CancellationToken ct)
    {
        var story = await stories.BeginConversionAsync(number, actor, ct);
        DevOpsCardWriteResult created;
        try
        {
            var settings = await rules.SettingsOnlyAsync(ct);
            settings.TryGetValue(SkillsConfigurationKeys.DeskStoryFieldMap, out var json);
            var map = DeskStoryFieldMap.For(json, story.Type);
            var epic = await epics.FindAsync(story.EpicNumber, ct);
            var create = DeskStoryFieldMap.BuildRequest(story, map, request, epic?.DevOpsTeam, epic?.DevOpsBoard);
            var origin = $"a partir da história #{story.Number}";
            created = await runner.CreateAsync(create, user, ct, string.IsNullOrWhiteSpace(via) ? origin : $"{origin}, {via}");
        }
        catch
        {
            await stories.AbortConversionAsync(number, CancellationToken.None);
            throw;
        }

        var cardId = created.Card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            var converted = await stories.CompleteConversionAsync(number, cardId, actor, CancellationToken.None);
            return new EpicStoryConversionResult(converted, created.Card, created.Summary);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // O card existe no DevOps: a trava fica (vence em EpicStory.ConversionLock) e o log diz qual card é o da história.
            logger.LogError(e, "História #{Story} virou o card {Card} no DevOps, mas não foi marcada como convertida", number, cardId);
            throw;
        }
    }
}
