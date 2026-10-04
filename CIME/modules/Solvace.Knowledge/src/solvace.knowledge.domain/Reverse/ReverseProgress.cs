using System.Text.Json;
using System.Text.RegularExpressions;

namespace solvace.knowledge.domain.Reverse;

public sealed class ReverseProgressStep
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>pending | running | completed | failed | skipped</summary>
    public string Status { get; set; } = "pending";
    public string? Detail { get; set; }
    /// <summary>Etapa-mãe (as áreas da leitura ficam sob "leitura").</summary>
    public string? Parent { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class ReverseProgressLog
{
    public DateTimeOffset At { get; set; }
    public string Text { get; set; } = string.Empty;
    /// <summary>info | progress | warning | error</summary>
    public string Kind { get; set; } = "info";
}

/// <summary>Atualização enviada pela skill (re.sh): etapa, atividade do momento e/ou registro.</summary>
public sealed class ReverseProgressUpdate
{
    public string? Step { get; set; }
    public string? Status { get; set; }
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public string? Parent { get; set; }
    public string? Activity { get; set; }
    public string? Log { get; set; }
    public string? Kind { get; set; }
}

/// <summary>
/// Andamento da sessão do Claude que escreve o documento (0052): etapas fixas (sessão, inventário, leitura e escrita —
/// com uma subetapa por área —, checagem, envio), o que está fazendo agora e um registro curto. A tela mostra ao vivo.
/// </summary>
public sealed partial class ReverseProgress
{
    public const int MaxLogs = 80;
    public const int MaxSteps = 60;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Statuses = ["pending", "running", "completed", "failed", "skipped"];

    public List<ReverseProgressStep> Steps { get; set; } = [];
    public string? Activity { get; set; }
    public DateTimeOffset? ActivityAt { get; set; }
    public List<ReverseProgressLog> Logs { get; set; } = [];

    public static ReverseProgress Initial(DateTimeOffset now) => new()
    {
        Steps =
        [
            new() { Key = "sessao", Title = "Sessão aberta (modelo, publicado, sugestões, anexos)", Status = "completed", StartedAt = now, FinishedAt = now },
            new() { Key = "inventario", Title = "Inventário do código" },
            new() { Key = "banco", Title = "Banco de dados (DEMO)" },
            new() { Key = "leitura", Title = "Leitura do código e escrita" },
            new() { Key = "checagem", Title = "Checagem e cobertura" },
            new() { Key = "envio", Title = "Enviado para revisão" }
        ]
    };

    public static ReverseProgress Parse(string? json, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(json)) return Initial(now);
        try { return JsonSerializer.Deserialize<ReverseProgress>(json, Json) ?? Initial(now); }
        catch (JsonException) { return Initial(now); }
    }

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public void Apply(ReverseProgressUpdate update, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(update.Step))
        {
            var key = Regex.Replace(update.Step.Trim().ToLowerInvariant(), @"[^a-z0-9:._-]+", "-");
            var step = Steps.FirstOrDefault(s => s.Key == key);
            if (step is null)
            {
                if (Steps.Count >= MaxSteps) throw new Entities.DomainException($"No máximo {MaxSteps} etapas no andamento.");
                var parent = string.IsNullOrWhiteSpace(update.Parent) ? (key.StartsWith("area:") ? "leitura" : null) : update.Parent.Trim().ToLowerInvariant();
                step = new ReverseProgressStep { Key = key, Title = Clean(update.Title, 160) ?? key, Parent = parent };
                // Subetapa entra depois da última irmã (ou logo depois da mãe); etapa nova sem mãe, antes da checagem.
                var anchor = parent is null ? Steps.FindIndex(s => s.Key == "checagem") - 1 : Steps.FindLastIndex(s => s.Key == parent || s.Parent == parent);
                Steps.Insert(anchor < 0 ? Steps.Count : anchor + 1, step);
            }
            else if (Clean(update.Title, 160) is { } title) step.Title = title;
            if (Clean(update.Detail, 400) is { } detail) step.Detail = detail;
            var status = (update.Status ?? string.Empty).Trim().ToLowerInvariant();
            if (status.Length > 0)
            {
                if (!Statuses.Contains(status)) throw new Entities.DomainException("Status da etapa: pending, running, completed, failed ou skipped.");
                if (status == "running" && step.Status != "running") step.StartedAt = now;
                if (status is "completed" or "failed" or "skipped") step.FinishedAt = now;
                step.Status = status;
                // a etapa-mãe anda junto com a primeira filha
                if (status == "running" && step.Parent is { } p && Steps.FirstOrDefault(s => s.Key == p) is { Status: "pending" } mother)
                {
                    mother.Status = "running";
                    mother.StartedAt = now;
                }
            }
        }
        if (Clean(update.Activity, 200) is { } activity)
        {
            Activity = activity;
            ActivityAt = now;
        }
        if (Clean(update.Log, 500) is { } log)
        {
            var kind = (update.Kind ?? "info").Trim().ToLowerInvariant();
            Logs.Add(new ReverseProgressLog { At = now, Text = log, Kind = kind is "progress" or "warning" or "error" ? kind : "info" });
            if (Logs.Count > MaxLogs) Logs.RemoveRange(0, Logs.Count - MaxLogs);
        }
    }

    /// <summary>Texto curto, sem quebras e sem cara de segredo (o andamento é visível a todos).</summary>
    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = Regex.Replace(value.Trim(), @"\s+", " ");
        if (Secret().IsMatch(v)) v = Secret().Replace(v, "***");
        return v.Length <= max ? v : v[..max] + "…";
    }

    [GeneratedRegex(@"(?i)(password|pwd|senha|secret|token|apikey|api_key)\s*[=:]\s*\S+|\beyJ[\w-]{10,}\.[\w-]{10,}\.[\w-]{10,}")]
    private static partial Regex Secret();
}
