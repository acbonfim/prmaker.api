using System.Text.Json.Nodes;

namespace solvace.prform.Skills;

/// <summary>
/// 0085 r3: tipo do campo de cada param dos tipos de sub-tarefa no editor do item de trabalho (<c>input</c>:
/// repository|branch|target-branch|state|commits|text|textarea|select|pull-request, com <c>options</c> no select). Config antiga sem
/// <c>input</c> ganha o padrão pela chave do param — o mesmo mapeamento da migração <c>SeedWorkItemKinds0085b</c> (mude os dois).
/// Param que já tem <c>input</c> fica como o admin deixou.
/// </summary>
public static class WorkItemParamInputs
{
    public const string Repository = "repository";
    public const string Branch = "branch";
    public const string TargetBranch = "target-branch";
    public const string State = "state";
    public const string Commits = "commits";
    public const string Text = "text";
    public const string Textarea = "textarea";
    public const string Select = "select";
    /// <summary>r4: PR do card / aberto no repositório (kind pr-review).</summary>
    public const string PullRequest = "pull-request";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { Repository, Branch, TargetBranch, State, Commits, Text, Textarea, Select, PullRequest };

    /// <summary>Input padrão (e opções, quando é select) do param <paramref name="key"/> do tipo <paramref name="kind"/>.</summary>
    public static (string Input, string[]? Options) Default(string? kind, string? key) =>
        (key?.Trim().ToLowerInvariant(), kind?.Trim().ToLowerInvariant()) switch
        {
            ("repository", _) => (Repository, null),
            ("branch" or "source" or "from", _) => (Branch, null),
            ("target", "comment") => (Select, ["timeline", "devops"]),
            ("target", _) => (TargetBranch, null),
            ("state", _) => (State, null),
            ("commits", _) => (Commits, null),
            ("pullrequest" or "pull-request" or "pr", _) => (PullRequest, null),
            ("focus", _) => (Textarea, null),
            ("draft", _) => (Select, ["false", "true"]),
            ("priority", _) => (Select, ["Baixa", "Média", "Alta"]),
            ("description" or "instructions" or "message" or "text" or "how", _) => (Textarea, null),
            _ => (Text, null)
        };

    /// <summary>
    /// Preenche <c>input</c>/<c>options</c> dos params que não têm. O param <c>system</c> (sistema de chamados) sem opções vira
    /// select com o <c>TicketSystem</c> configurado (separado por vírgula/ponto e vírgula) ou texto, se não houver.
    /// </summary>
    public static void Apply(JsonArray kinds, string? ticketSystem, IReadOnlyList<ValidationEnvironment>? environments = null)
    {
        var systems = (ticketSystem ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var kind in kinds.OfType<JsonObject>())
        {
            if (kind["params"] is not JsonArray parameters) continue;
            var kindName = Str(kind, "kind");
            foreach (var param in parameters.OfType<JsonObject>())
            {
                var key = Str(param, "key");
                if (string.Equals(key, "system", StringComparison.OrdinalIgnoreCase))
                {
                    FillSystem(param, systems);
                    continue;
                }
                // 0092: ambiente da validação = um dos ambientes configurados (o select mantém um valor fora da lista).
                if (string.Equals(key, "environment", StringComparison.OrdinalIgnoreCase) && environments is { Count: > 0 }
                    && Str(param, "input") is null or Select && param["options"] is null)
                {
                    param["input"] = Select;
                    param["options"] = ValidationEnvironments.Options(environments);
                    continue;
                }
                if (Str(param, "input") is { Length: > 0 }) continue;
                var (input, options) = Default(kindName, key);
                param["input"] = input;
                if (options is not null && param["options"] is null)
                    param["options"] = new JsonArray(options.Select(o => (JsonNode?)JsonValue.Create(o)).ToArray());
            }
        }
    }

    private static void FillSystem(JsonObject param, string[] systems)
    {
        var input = Str(param, "input");
        var hasOptions = param["options"] is JsonArray { Count: > 0 };
        if (input is { Length: > 0 } && !string.Equals(input, Select, StringComparison.OrdinalIgnoreCase)) return;
        if (hasOptions) return;
        if (systems.Length > 0)
        {
            param["input"] = Select;
            param["options"] = new JsonArray(systems.Select(o => (JsonNode?)JsonValue.Create(o)).ToArray());
        }
        else
        {
            param["input"] = Text; // select sem opções não serve: sem TicketSystem, campo livre
        }
    }

    private static string? Str(JsonObject obj, string name) =>
        obj[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
