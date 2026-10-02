using System.Text.Json;
using System.Text.RegularExpressions;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0050: uma atividade do Claude numa execução ("Consultando o banco", "Lendo Foo.cs") — o executor monta o rótulo a
/// partir do <c>tool_use</c> do stream e nunca manda o comando cru; aqui ainda cortamos e descartamos o que parecer
/// segredo (defesa em profundidade: a tela de quem acompanha o card mostra o rótulo).
/// </summary>
public sealed partial class ExecutionActivity
{
    public const int MaxLabelLength = 120;
    public const int MaxToolLength = 60;
    public const int MaxRecent = 10;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Label { get; set; } = string.Empty;
    /// <summary>Ferramenta de origem (Bash, Read, Grep, mcp__prmake__…) — o ícone da tela.</summary>
    public string? Tool { get; set; }
    public DateTimeOffset At { get; set; }

    /// <summary>Rótulo limpo, ou null quando não sobra nada aproveitável (vazio ou com cara de segredo).</summary>
    public ExecutionActivity? Normalize()
    {
        var label = Regex.Replace(Label ?? string.Empty, @"\s+", " ").Trim();
        if (label.Length == 0 || LooksSecret(label)) return null;
        if (label.Length > MaxLabelLength) label = label[..(MaxLabelLength - 1)] + "…";
        var tool = Tool?.Trim();
        if (string.IsNullOrEmpty(tool)) tool = null;
        else if (tool.Length > MaxToolLength) tool = tool[..MaxToolLength];
        return new ExecutionActivity { Label = label, Tool = tool, At = At.ToUniversalTime() };
    }

    public static bool LooksSecret(string text) => SecretPattern().IsMatch(text);

    public static string Serialize(IEnumerable<ExecutionActivity> items) => JsonSerializer.Serialize(items, Json);

    public static List<ExecutionActivity> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<ExecutionActivity>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // senha/token/connection string/-p<senha> do mysql/psql/sqlcmd, chaves de API.
    [GeneratedRegex(@"(?ix)
        (pass(word)?|pwd|secret|token|api[-_]?key|bearer|authorization)\s*[=:]
      | \b(server|host|data\s+source|user\s+id|uid)\s*=\s*[^;\s]+\s*;
      | (^|\s)-p\S+
      | \bPGPASSWORD\b | \bMYSQL_PWD\b
      | \b(sk|ghp|gho|xox[abp])[-_][A-Za-z0-9_-]{10,}
      | \beyJ[A-Za-z0-9_-]{10,}\.
    ")]
    private static partial Regex SecretPattern();
}
