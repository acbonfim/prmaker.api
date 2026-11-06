using System.Text.Json;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Chamado na Bancada: o chamado que a pessoa anexa a uma sub-tarefa <c>ticket</c> de item avulso fica nas saídas da
/// sub-tarefa (<c>{"ticket": {url, title, status}}</c>) — no item espelho do plano ele é um link da etapa
/// (<see cref="ExecutionLink"/>, bloqueante). Mesma regra da etapa do plano (<see cref="ExecutionPlan.ApplyTicketState"/>):
/// aberto → aguardando; resolvido → concluída; fechado sem resolução → volta para a pessoa.
/// </summary>
public static class WorkTaskTicket
{
    public const string OutputKey = "ticket";
    private const int MaxTitleLength = 200;
    private const int MaxUrlLength = 2000;

    public static WorkTaskLinkResponse? Read(WorkItemTask task)
    {
        var output = task.OutputElement;
        if (output.ValueKind != JsonValueKind.Object || !output.TryGetProperty(OutputKey, out var t) || t.ValueKind != JsonValueKind.Object)
            return null;
        var url = Text(t, "url");
        if (string.IsNullOrWhiteSpace(url)) return null;
        return new WorkTaskLinkResponse
        {
            Kind = ExecutionLinkKind.Ticket,
            Url = url,
            Title = Text(t, "title"),
            Status = Text(t, "status") ?? ExecutionLinkStatus.Open,
        };
    }

    /// <summary>Saídas com o chamado (mescladas às que a sub-tarefa já tem).</summary>
    public static string Output(string url, string? title, string status) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            [OutputKey] = new Dictionary<string, string?> { ["url"] = url, ["title"] = title, ["status"] = status },
        });

    public static string NormalizeUrl(string? url)
    {
        var u = (url ?? string.Empty).Trim();
        if (u.Length == 0) throw new DomainException("Informe o link (ou o número) do chamado.");
        if (u.Length > MaxUrlLength) throw new DomainException($"O link do chamado passa de {MaxUrlLength} caracteres.");
        return u;
    }

    public static string? NormalizeTitle(string? title)
    {
        var t = title?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length <= MaxTitleLength ? t : t[..MaxTitleLength];
    }

    public static string NormalizeStatus(string? status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (!ExecutionLinkStatus.TicketStatuses.Contains(s))
            throw new DomainException($"Status de chamado inválido: '{status}' (use open, resolved ou closed).");
        return s;
    }

    public static string DisplayName(string url, string? title) => string.IsNullOrWhiteSpace(title) ? url : title!;

    /// <summary>Ação na sub-tarefa e o resultado para o status do chamado.</summary>
    public static (string Action, string Result) TaskAction(string status, string display) => status switch
    {
        ExecutionLinkStatus.Resolved => (WorkTaskAction.Complete, $"Chamado {display} resolvido"),
        ExecutionLinkStatus.Closed => (WorkTaskAction.Wait, $"Chamado {display} fechado sem resolução — abra outro chamado ou conclua a sub-tarefa"),
        _ => (WorkTaskAction.Wait, $"Aguardando o chamado {display}"),
    };

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
