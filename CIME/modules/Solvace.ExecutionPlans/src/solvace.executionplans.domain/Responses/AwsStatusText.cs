using System.Globalization;
using System.Text;
using System.Text.Json;
using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

/// <summary>
/// 0100: texto curto do <c>prmake_aws_status</c> (executor e Especialista do caso): por ambiente o veredito e as pipelines
/// (status, última execução, commit implantado) + quando e por quem foi coletado. Lê o retrato como o script mandou e
/// ignora o que não entender.
/// </summary>
public static class AwsStatusText
{
    public const int MaxPipelinesPerEnvironment = 8;

    public static string Empty(string card) =>
        $"Sem retrato da AWS para o card {card} — peça a coleta pela aba AWS (Atualizar) ou rode " +
        $"`bash ~/.claude/skills/aws-deploy/scripts/aws.sh collect {card}`.";

    public static string Format(string card, string? snapshotJson, DateTimeOffset? collectedAt, string? collectedBy, AwsRefresh? refresh = null)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return Empty(card) + RefreshLine(refresh);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(snapshotJson); }
        catch (JsonException) { return Empty(card) + RefreshLine(refresh); }

        using (doc)
        {
            var root = doc.RootElement;
            var sb = new StringBuilder();
            sb.Append($"AWS do card {card} — coletado em {When(collectedAt) ?? When(Date(root, "collectedAt")) ?? "?"}");
            if (!string.IsNullOrWhiteSpace(collectedBy)) sb.Append($" por {collectedBy}");
            sb.AppendLine(".");

            if (root.TryGetProperty("environments", out var envs) && envs.ValueKind == JsonValueKind.Array && envs.GetArrayLength() > 0)
            {
                foreach (var env in envs.EnumerateArray())
                {
                    var label = Str(env, "label") ?? Str(env, "key") ?? "?";
                    var message = Str(env, "message") ?? VerdictText(Str(env, "verdict"), label);
                    sb.AppendLine($"- {label}: {message}");
                    if (Str(env, "detail") is { Length: > 0 } detail)
                        sb.AppendLine($"  {detail}");
                    if (!env.TryGetProperty("pipelines", out var pipes) || pipes.ValueKind != JsonValueKind.Array) continue;
                    var count = 0;
                    foreach (var p in pipes.EnumerateArray())
                    {
                        if (++count > MaxPipelinesPerEnvironment) continue;
                        var parts = new List<string> { Str(p, "status") ?? "sem execução" };
                        if (When(Date(p, "lastRunAt")) is { } last) parts.Add($"última {last}");
                        if (Str(p, "deployedSha") is { Length: > 0 } sha) parts.Add($"commit {Short(sha)}");
                        if (Str(p, "verdict") == "broken") parts.Add("esteira quebrada antes do nosso código (não é do card)");
                        else if (p.TryGetProperty("deployed", out var dep) && dep.ValueKind is JsonValueKind.True) parts.Add("com o nosso código");
                        else if (p.TryGetProperty("inBranch", out var inb) && inb.ValueKind is JsonValueKind.True) parts.Add("nosso código só na branch");
                        var app = Str(p, "app") is { Length: > 0 } a && a != Str(p, "name") ? $" ({a})" : "";
                        sb.AppendLine($"  - {Str(p, "name") ?? "?"}{app}: {string.Join(", ", parts)}");
                    }
                    if (count > MaxPipelinesPerEnvironment)
                        sb.AppendLine($"  - (+{count - MaxPipelinesPerEnvironment} pipelines — veja a aba AWS)");
                }
            }
            else
            {
                sb.AppendLine("- Nenhum ambiente no retrato (o card não tem pipelines conhecidas ou a coleta falhou).");
            }

            if (root.TryGetProperty("denied", out var denied) && denied.ValueKind == JsonValueKind.Array && denied.GetArrayLength() > 0)
                sb.AppendLine($"Sem permissão nesta conta: {string.Join(", ", denied.EnumerateArray().Select(d => d.ToString()))}.");
            sb.Append(RefreshLine(refresh));
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>Texto fixo do veredito (quando o retrato não trouxe <c>message</c>).</summary>
    public static string VerdictText(string? verdict, string label) => verdict switch
    {
        AwsVerdict.Deployed => $"Nosso código está em {label}",
        AwsVerdict.Absent => $"Ainda não está em {label}",
        AwsVerdict.Partial => $"Parte do nosso código está em {label} (o ajuste mais recente ainda não)",
        AwsVerdict.Waiting => $"Na branch de {label}, aguardando o deploy",
        AwsVerdict.Deploying => $"Deploy em andamento em {label}",
        AwsVerdict.Failed => $"Deploy falhou em {label}",
        AwsVerdict.NotApplicable => "Não se aplica",
        AwsVerdict.Broken => $"Ainda não está em {label} (esteira quebrada antes do nosso código)",
        _ => "Sem informação (sem permissão ou erro na coleta)",
    };

    private static string RefreshLine(AwsRefresh? r) => r is null || !r.IsActive ? string.Empty
        : $"\nColeta {(r.Status == AwsRefreshStatus.Claimed ? "em andamento" : "na fila")} (pedida por {r.RequestedByName}).";

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    private static string? When(DateTimeOffset? d) =>
        d?.ToUniversalTime().ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string Short(string sha) => sha.Length > 8 ? sha[..8] : sha;
}
