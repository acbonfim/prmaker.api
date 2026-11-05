namespace solvace.prform.PluginSettings;

/// <summary>Tipos de editor das chaves (0086) — o valor gravado é sempre texto; o tipo só diz como editar e validar.</summary>
public static class SettingTypes
{
    public const string Text = "text";
    public const string Textarea = "textarea";
    public const string Secret = "secret";
    public const string Bool = "bool";
    public const string Number = "number";
    public const string Enum = "enum";
    public const string List = "list";
    public const string Steps = "steps";
    public const string Map = "map";
    public const string Table = "table";
    public const string Pattern = "pattern";
    public const string Prompt = "prompt";
    public const string Json = "json";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
        { Text, Textarea, Secret, Bool, Number, Enum, List, Steps, Map, Table, Pattern, Prompt, Json };

    /// <summary>Tipos de coluna de uma tabela.</summary>
    public static readonly IReadOnlySet<string> ColumnTypes = new HashSet<string> { Text, Textarea, Number, Bool, Enum, List };

    /// <summary>Tipos do valor de um mapa.</summary>
    public static readonly IReadOnlySet<string> MapValueTypes = new HashSet<string> { Text, Textarea, List };
}

/// <summary>Quem lê a chave (0086).</summary>
public static class SettingReaders
{
    public const string Api = "api";
    public const string Skills = "skills";
    public const string Front = "front";
    public const string Executor = "executor";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Api, Skills, Front, Executor };
}

public sealed record SettingOption(string Value, string Label);

public sealed record SettingVariable(string Name, string Description, string? Example = null);

/// <summary>Coluna de uma tabela (type: text|textarea|number|bool|enum|list; width: sm|md|lg; allowCustom: enum aceita valor livre).</summary>
public sealed record SettingColumn(string Key, string Label, string Type = SettingTypes.Text, bool Required = false,
    IReadOnlyList<SettingOption>? Options = null, string? Width = null, bool AllowCustom = false);

/// <summary>Plugin onde a chave fica: pelo nome exato (Description) ou, nos plugins 9 e 11, pelo id.</summary>
public sealed record SettingPluginRef(string Name, int? Id = null)
{
    public bool Matches(solvace.prform.domain.Entities.Plugin plugin) =>
        Id is { } id ? plugin.Id == id : string.Equals(plugin.Description?.Trim(), Name, StringComparison.OrdinalIgnoreCase);
}

public sealed record SettingGroupDef(string Key, string Title, string? Description = null);

public sealed record SettingSectionDef(string Key, string Title, string Icon, string Description, IReadOnlyList<SettingGroupDef> Groups);

/// <summary>Uma chave do catálogo (0086): descreve, não guarda valor.</summary>
public sealed class SettingDef
{
    public required string Section { get; init; }
    public required string Group { get; init; }
    public required SettingPluginRef Plugin { get; init; }
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Help { get; init; }
    public required string Type { get; init; }

    /// <summary>Padrão de reserva do código (null = sem padrão: vazio/ausente desliga ou cai no padrão de quem lê).</summary>
    public string? Default { get; init; }

    public IReadOnlyList<SettingOption>? Options { get; init; }
    public bool AllowCustom { get; init; }
    public decimal? Min { get; init; }
    public decimal? Max { get; init; }
    public string? Unit { get; init; }

    /// <summary>list/steps: "csv" (a,b,c) ou "json" (["a","b"]).</summary>
    public string? ListFormat { get; init; }

    public IReadOnlyList<string>? StepSources { get; init; }
    public IReadOnlyList<SettingVariable>? Variables { get; init; }
    public IReadOnlyList<SettingColumn>? Columns { get; init; }

    /// <summary>table cujo valor é objeto {chave: {...}}: a coluna que vira a chave.</summary>
    public string? RowKey { get; init; }

    public string? MapKeyLabel { get; init; }
    public string? MapValueLabel { get; init; }
    public string? MapValueType { get; init; }

    /// <summary>json: raiz esperada ("object" | "array"; null = qualquer).</summary>
    public string? JsonRoot { get; init; }

    public required IReadOnlyList<string> ReadBy { get; init; }
    public bool Obsolete { get; init; }
    public bool Advanced { get; init; }
    public bool MultilineHint { get; init; }

    /// <summary>Raiz JSON que o valor precisa ter (null = não é JSON).</summary>
    public string? ExpectedJsonRoot => Type switch
    {
        SettingTypes.Table => RowKey is null ? "array" : "object",
        SettingTypes.Map => "object",
        SettingTypes.List or SettingTypes.Steps when ListFormat == "json" => "array",
        SettingTypes.Json => JsonRoot ?? "any",
        _ => null
    };
}

// ---------------------------------------------------------------- respostas (contrato do plan.md)

public sealed class SettingsResponse
{
    public List<SettingSectionResponse> Sections { get; set; } = [];
    public List<SettingPluginResponse> Plugins { get; set; } = [];
    public List<UncatalogedSettingResponse> Uncataloged { get; set; } = [];
    public List<string> MissingPlugins { get; set; } = [];
}

public sealed class SettingSectionResponse
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<SettingGroupResponse> Groups { get; set; } = [];
}

public sealed class SettingGroupResponse
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<SettingFieldResponse> Fields { get; set; } = [];
}

public sealed class SettingFieldResponse
{
    public string Id { get; set; } = string.Empty;
    public int? PluginId { get; set; }
    public string PluginName { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Help { get; set; }
    public string Type { get; set; } = SettingTypes.Text;
    public string? Value { get; set; }
    public bool HasValue { get; set; }
    public bool Exists { get; set; }
    public string? Default { get; set; }
    public bool IsDefault { get; set; }
    public IReadOnlyList<SettingOption>? Options { get; set; }
    public bool AllowCustom { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public string? Unit { get; set; }
    public string? ListFormat { get; set; }
    public IReadOnlyList<string>? StepSources { get; set; }
    public IReadOnlyList<SettingVariable>? Variables { get; set; }
    public IReadOnlyList<SettingColumn>? Columns { get; set; }
    public string? RowKey { get; set; }
    public string? MapKeyLabel { get; set; }
    public string? MapValueLabel { get; set; }
    public string? MapValueType { get; set; }
    public IReadOnlyList<string> ReadBy { get; set; } = [];
    public SettingPersonalResponse? Personal { get; set; }
    public bool Obsolete { get; set; }
    public bool Advanced { get; set; }
    public bool MultilineHint { get; set; }
}

public sealed class SettingPersonalResponse
{
    public bool UserFills { get; set; }
    public bool Required { get; set; }
    public bool UseGlobalDefault { get; set; }
}

public sealed class SettingPluginResponse
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public bool IsPersonal { get; set; }
    public bool IsOptional { get; set; }
    public bool AdminOnly { get; set; }
    public string? Version { get; set; }
}

public sealed class UncatalogedSettingResponse
{
    public int PluginId { get; set; }
    public string PluginName { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool Secret { get; set; }
}

// ---------------------------------------------------------------- PATCH

public sealed class SettingsPatchRequest
{
    public List<SettingChangeRequest> Changes { get; set; } = [];
}

public sealed class SettingChangeRequest
{
    public int PluginId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }

    /// <summary>Valor que a tela leu (null = a chave não existia). Diferente do atual → 409.</summary>
    public string? Expected { get; set; }

    public bool Remove { get; set; }
}

public sealed record SettingConflict(string Id, string Key, int PluginId, string? Current, string? Expected);

public sealed record SettingError(string Id, string Message);

public sealed record SettingPluginVersion(int Id, string Version);

public sealed class SettingsPatchResponse
{
    public List<SettingPluginVersion> Plugins { get; set; } = [];
    public List<SettingFieldResponse> Fields { get; set; } = [];
}

public sealed class SettingsConflictResponse
{
    public List<SettingConflict> Conflicts { get; set; } = [];
}

public sealed class SettingsErrorResponse
{
    public List<SettingError> Errors { get; set; } = [];
}

// ---------------------------------------------------------------- histórico

public sealed class SettingHistoryResponse
{
    public int PluginId { get; set; }
    public string? PluginName { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? ChangedBy { get; set; }
    public string? ChangedByName { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
