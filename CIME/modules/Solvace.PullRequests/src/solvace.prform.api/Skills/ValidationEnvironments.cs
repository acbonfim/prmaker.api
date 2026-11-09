using System.Text.Json;
using System.Text.Json.Nodes;

namespace solvace.prform.Skills;

/// <summary>Ambiente de validação conhecido (<c>ValidationEnvironments.environments[]</c>).</summary>
public sealed record ValidationEnvironment(string Key, string Label, string Url, bool ReadOnly, string? Kind, string? User);

/// <summary>
/// 0092: ambientes onde a validação de uma demanda roda (item de validação com uma sub-tarefa <c>env-validation</c> por
/// ambiente) — chave <c>ValidationEnvironments</c> do "Skills Configurations". A skill <c>validar-ambiente</c> lê a mesma
/// chave (URL, usuário padrão, banco para achar plantas/admins, somente leitura); o PRMake usa as opções do modelo
/// <c>validar-em-ambiente</c>. Senha nunca fica aqui: só na máquina de quem valida.
/// </summary>
public static class ValidationEnvironments
{
    /// <summary>Marcador das opções do ask: os ambientes como <c>{value, label}</c>.</summary>
    public const string OptionsMarker = "{validationEnvironments}";

    /// <summary>Input do ask/parâmetro que aceita vários ambientes (separados por vírgula) e valor livre (nome ou URL).</summary>
    public const string Input = "environments";

    /// <summary>Padrão semeado (o mesmo da migração <c>SeedEnvValidation0092</c>).</summary>
    public const string DefaultJson = solvace.prform.infra.Migrations.SeedEnvValidation0092.Environments;

    /// <summary>Ambientes da configuração (JSON inválido ou vazio → os padrão). Sem <c>key</c> ou <c>url</c> fica de fora.</summary>
    public static IReadOnlyList<ValidationEnvironment> Parse(string? json)
    {
        var list = TryParse(json);
        return list is { Count: > 0 } ? list : TryParse(DefaultJson) ?? [];
    }

    private static List<ValidationEnvironment>? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var root = JsonNode.Parse(json);
            var envs = root switch
            {
                JsonObject o => o["environments"] as JsonArray,
                JsonArray a => a,
                _ => null
            };
            var productionReadOnly = root is JsonObject ro && ro["productionReadOnly"] is JsonValue pv && pv.TryGetValue<bool>(out var p) ? p : true;
            if (envs is null) return null;
            var result = new List<ValidationEnvironment>();
            foreach (var e in envs.OfType<JsonObject>())
            {
                var key = Str(e, "key");
                var url = Str(e, "url");
                if (key is null || url is null) continue;
                var kind = Str(e, "kind");
                var readOnly = e["readOnly"] is JsonValue rv && rv.TryGetValue<bool>(out var r)
                    ? r
                    : productionReadOnly && string.Equals(kind, "production", StringComparison.OrdinalIgnoreCase);
                result.Add(new ValidationEnvironment(key, Str(e, "label") ?? key, url, readOnly, kind, Str(e, "user")));
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Ambientes sugeridos por tipo do card (<c>defaults: {"bug": ["dev","qa"]}</c>) como texto "dev, qa".</summary>
    public static IReadOnlyDictionary<string, string> Defaults(string? json)
    {
        var result = DefaultsOf(json);
        return result.Count > 0 ? result : DefaultsOf(DefaultJson);
    }

    private static Dictionary<string, string> DefaultsOf(string? json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            if (JsonNode.Parse(json) is JsonObject root && root["defaults"] is JsonObject defaults)
                foreach (var (type, value) in defaults)
                {
                    var keys = value switch
                    {
                        JsonArray a => a.OfType<JsonValue>().Select(x => x.TryGetValue<string>(out var k) ? k.Trim() : null),
                        JsonValue v when v.TryGetValue<string>(out var t) => t.Split([',', ';'], StringSplitOptions.TrimEntries),
                        _ => []
                    };
                    var text = string.Join(", ", keys.Where(k => !string.IsNullOrEmpty(k)));
                    if (text.Length > 0) result[type] = text;
                }
        }
        catch (JsonException)
        {
        }
        return result;
    }

    /// <summary>Rótulo da opção na tela: "Desenvolvimento · devsp.solvacelabs.com" (+ " · somente leitura").</summary>
    public static string OptionLabel(ValidationEnvironment env)
    {
        var host = Uri.TryCreate(env.Url, UriKind.Absolute, out var u) ? u.Host : env.Url.Trim().TrimEnd('/');
        return $"{env.Label} · {host}{(env.ReadOnly ? " · somente leitura" : string.Empty)}";
    }

    /// <summary>Opções do ask (<c>{value, label}</c>).</summary>
    public static JsonArray Options(IEnumerable<ValidationEnvironment> envs) =>
        new(envs.Select(e => (JsonNode?)new JsonObject { ["value"] = e.Key, ["label"] = OptionLabel(e) }).ToArray());

    /// <summary>Rótulo para o <c>{eachLabel}</c>: o ambiente conhecido pela chave ou pela URL; senão o próprio valor.</summary>
    public static string LabelOf(IEnumerable<ValidationEnvironment> envs, string value)
    {
        var v = value.Trim();
        var hit = envs.FirstOrDefault(e => string.Equals(e.Key, v, StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(e.Url.TrimEnd('/'), v.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        return hit?.Label ?? v;
    }

    private static string? Str(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
}
