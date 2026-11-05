using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonException = System.Text.Json.JsonException;

namespace solvace.prform.PluginSettings;

/// <summary>
/// 0086: regras puras das configurações (sem banco) — validação por tipo, "é o padrão?", conflito pelo valor esperado
/// e a mescla das chaves no <c>Options</c> (<c>[{...}]</c>) preservando a ordem e o que não foi enviado.
/// </summary>
public static class PluginSettingsRules
{
    public const int MaxKeyLength = 200;

    // ------------------------------------------------------------------ validação

    /// <summary>Mensagem de erro do valor para o tipo do catálogo (null = válido). Chave fora do catálogo: só texto.</summary>
    public static string? Validate(SettingDef? def, string? value)
    {
        if (value is null)
            return "Informe o valor (ou remova a chave).";
        if (def is null || string.IsNullOrWhiteSpace(value))
            return null; // vazio = o leitor usa o padrão dele (ou desliga, quando a ajuda diz)

        var v = value.Trim();
        switch (def.Type)
        {
            case SettingTypes.Secret when v == domain.Entities.PluginSettingChange.SecretMask:
                return "Informe o novo valor do segredo (a máscara não é um valor).";
            case SettingTypes.Bool:
                return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("false", StringComparison.OrdinalIgnoreCase)
                    ? null : "Use true ou false.";
            case SettingTypes.Number:
                if (!TryNumber(v, out var n))
                    return "Número inválido (use ponto para decimais, ex.: 0.9).";
                if (def.Min is { } min && n < min)
                    return $"O mínimo é {Format(min)}.";
                if (def.Max is { } max && n > max)
                    return $"O máximo é {Format(max)}.";
                return null;
            case SettingTypes.Enum when !def.AllowCustom:
                return def.Options?.Any(o => string.Equals(o.Value, v, StringComparison.OrdinalIgnoreCase)) == true
                    ? null : $"Escolha uma das opções: {string.Join(", ", def.Options?.Select(o => o.Value) ?? [])}.";
        }

        return def.ExpectedJsonRoot is { } root ? ValidateJson(def, v, root) : null;
    }

    private static string? ValidateJson(SettingDef def, string value, string root)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(value);
        }
        catch (JsonException e)
        {
            return e.LineNumber is { } line
                ? $"JSON inválido (linha {line + 1}, posição {(e.BytePositionInLine ?? 0) + 1})."
                : "JSON inválido.";
        }

        switch (root)
        {
            case "array" when node is not JsonArray:
                return "O valor precisa ser uma lista JSON ([...]).";
            case "object" when node is not JsonObject:
                return "O valor precisa ser um objeto JSON ({...}).";
        }

        return def.Type switch
        {
            SettingTypes.List or SettingTypes.Steps => ((JsonArray)node!).All(i => i is JsonValue)
                ? null : "A lista só aceita textos ou números.",
            SettingTypes.Map => ValidateMap(def, (JsonObject)node!),
            SettingTypes.Table => ValidateTable(def, node!),
            _ => null
        };
    }

    private static string? ValidateMap(SettingDef def, JsonObject map)
    {
        foreach (var (key, item) in map)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "Há uma linha sem chave.";
            var ok = def.MapValueType == SettingTypes.List
                ? item is JsonArray list && list.All(i => i is JsonValue v && v.GetValueKind() == JsonValueKind.String)
                : item is JsonValue v2 && v2.GetValueKind() == JsonValueKind.String;
            if (!ok)
                return def.MapValueType == SettingTypes.List
                    ? $"\"{key}\": o valor precisa ser uma lista de textos."
                    : $"\"{key}\": o valor precisa ser um texto.";
        }
        return null;
    }

    private static string? ValidateTable(SettingDef def, JsonNode node)
    {
        var rows = new List<(string Label, JsonObject Row)>();
        if (def.RowKey is { } rowKey)
        {
            foreach (var (key, item) in (JsonObject)node)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return "Há uma linha sem chave.";
                if (item is not JsonObject obj)
                    return $"\"{key}\": cada linha precisa ser um objeto.";
                rows.Add((key, obj));
            }
        }
        else
        {
            var i = 0;
            foreach (var item in (JsonArray)node)
            {
                i++;
                if (item is not JsonObject obj)
                    return $"Linha {i}: cada linha precisa ser um objeto.";
                rows.Add(($"Linha {i}", obj));
            }
        }

        foreach (var (label, row) in rows)
        foreach (var column in def.Columns ?? [])
        {
            if (column.Key == def.RowKey)
                continue; // a coluna-chave é a chave do objeto
            var cell = row[column.Key];
            var empty = cell is null || (cell is JsonValue cv && cv.GetValueKind() == JsonValueKind.String && string.IsNullOrWhiteSpace(cv.GetValue<string>()));
            if (empty)
            {
                if (column.Required)
                    return $"{label}: preencha \"{column.Label}\".";
                continue;
            }
            if (column.Type == SettingTypes.Number && !(cell is JsonValue nv && (nv.GetValueKind() == JsonValueKind.Number
                    || (nv.GetValueKind() == JsonValueKind.String && TryNumber(nv.GetValue<string>(), out _)))))
                return $"{label}: \"{column.Label}\" precisa ser um número.";
            if (column.Type == SettingTypes.Bool && !(cell is JsonValue bv && bv.GetValueKind() is JsonValueKind.True or JsonValueKind.False))
                return $"{label}: \"{column.Label}\" precisa ser true ou false.";
        }
        return null;
    }

    // ------------------------------------------------------------------ padrão

    /// <summary>
    /// A chave está no padrão: não existe (o código usa a reserva), ou o valor equivale ao padrão. Sem padrão no
    /// catálogo, vazio conta como padrão.
    /// </summary>
    public static bool IsDefault(SettingDef def, bool exists, string? value)
    {
        if (!exists)
            return true;
        if (def.Default is null)
            return string.IsNullOrWhiteSpace(value);
        return Equivalent(def, value, def.Default);
    }

    /// <summary>Mesmo valor para o tipo: JSON normalizado, lista CSV sem espaços, número e booleano pelo valor.</summary>
    public static bool Equivalent(SettingDef def, string? a, string? b)
    {
        var x = NormalizeNewLines(a?.Trim() ?? string.Empty);
        var y = NormalizeNewLines(b?.Trim() ?? string.Empty);
        if (x == y)
            return true;

        switch (def.Type)
        {
            case SettingTypes.Bool:
            case SettingTypes.Enum:
                return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
            case SettingTypes.Number:
                return TryNumber(x, out var nx) && TryNumber(y, out var ny) && nx == ny;
            case SettingTypes.List or SettingTypes.Steps when def.ListFormat != "json":
                return SplitCsv(x).SequenceEqual(SplitCsv(y), StringComparer.Ordinal);
        }

        if (def.ExpectedJsonRoot is null)
            return false;
        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(x), JsonNode.Parse(y));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IEnumerable<string> SplitCsv(string value) =>
        value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ------------------------------------------------------------------ conflito

    /// <summary>O valor atual é o que a tela leu (null = a chave não existia)? Quebra de linha não conta.</summary>
    public static bool MatchesExpected(string? current, string? expected) =>
        current is null || expected is null
            ? current is null && expected is null
            : NormalizeNewLines(current) == NormalizeNewLines(expected);

    private static string NormalizeNewLines(string value) => value.Replace("\r\n", "\n");

    // ------------------------------------------------------------------ Options ([{...}])

    /// <summary>Lê o dicionário das chaves (o <c>[0]</c> do Options). Vazio = sem chaves; texto inválido lança.</summary>
    public static Dictionary<string, string> ReadValues(string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
            return new Dictionary<string, string>();
        var list = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(options)
                   ?? throw new InvalidOperationException("Options vazio.");
        return list.FirstOrDefault() ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// Mescla: só as chaves enviadas mudam. Mantém a ordem das chaves existentes; chave nova vai para o fim; remover tira a
    /// chave. Diferencia maiúsculas (como os leitores).
    /// </summary>
    public static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> current,
        IEnumerable<(string Key, string? Value, bool Remove)> changes)
    {
        var pending = new Dictionary<string, (string? Value, bool Remove)>(StringComparer.Ordinal);
        foreach (var (key, value, remove) in changes)
            pending[key] = (value, remove);

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in current)
        {
            if (!pending.Remove(key, out var change))
                merged[key] = value;
            else if (!change.Remove)
                merged[key] = change.Value ?? string.Empty;
        }
        foreach (var (key, change) in pending)
            if (!change.Remove)
                merged[key] = change.Value ?? string.Empty;
        return merged;
    }

    /// <summary>
    /// Grava as chaves no formato de sempre (<c>[{...}]</c>, compacto, como <c>new PluginConfiguration(...)</c>),
    /// preservando os demais itens da lista se houver.
    /// </summary>
    public static string WriteValues(string? originalOptions, IDictionary<string, string> values)
    {
        var first = JObject.FromObject(values);
        JArray array;
        try
        {
            array = string.IsNullOrWhiteSpace(originalOptions) ? [] : JArray.Parse(originalOptions);
        }
        catch (JsonReaderException)
        {
            array = [];
        }
        if (array.Count == 0)
            array.Add(first);
        else
            array[0] = first;
        return JsonConvert.SerializeObject(array);
    }

    // ------------------------------------------------------------------ números

    public static bool TryNumber(string? value, out decimal number) =>
        decimal.TryParse(value?.Trim(), NumberStyles.Number & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out number);

    private static string Format(decimal value) => value.ToString("0.############", CultureInfo.InvariantCulture);
}
