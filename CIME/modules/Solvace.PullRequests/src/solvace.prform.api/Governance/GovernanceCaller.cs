using System.Security.Claims;
using solvace.governance.application.Contracts;
using solvace.governance.domain;
using solvace.prform.Execution;

namespace solvace.prform.Governance;

/// <summary>
/// 0098: quem está agindo e de onde, sem ir ao banco (só claims e cabeçalhos): executor (credencial do executor),
/// especialista (token do caso — age "a pedido de" quem pediu), skill (<c>X-Execution-Client: skill</c>), MCP, tela
/// (navegador: cabeçalho Origin) ou API.
/// </summary>
public sealed record GovernanceCaller(GovernanceActor Actor, string Source, string? Role, string? Machine)
{
    public const string SkillHeader = "X-Execution-Client";

    public static GovernanceCaller From(HttpContext http, string? forcedSource = null)
    {
        var user = http.User;
        var roles = user.FindAll(ClaimTypes.Role).Concat(user.FindAll("role")).Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Guid? id = Guid.TryParse(user.FindFirst("ExternalId")?.Value, out var g) && g != Guid.Empty ? g : null;
        var name = user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst("name")?.Value ?? user.FindFirst("unique_name")?.Value;

        string source;
        if (CaseClaims.CardOf(user) is not null)
        {
            source = GovernanceSource.Specialist;
            // r5: o especialista age a pedido de alguém — a governança conta para quem pediu.
            if (Guid.TryParse(user.FindFirst(CaseClaims.RequestedById)?.Value, out var rid) && rid != Guid.Empty) id = rid;
            name = user.FindFirst(CaseClaims.RequestedByName)?.Value is { Length: > 0 } rn ? $"{rn} (via Especialista)" : name;
        }
        else if (user.FindFirst(ExecutorClaims.WorkerId) is not null) source = GovernanceSource.Executor;
        else if (forcedSource is not null) source = forcedSource;
        else if (string.Equals(http.Request.Headers[SkillHeader], "skill", StringComparison.OrdinalIgnoreCase)) source = GovernanceSource.Skill;
        else if (http.Request.Path.StartsWithSegments("/mcp")) source = GovernanceSource.Mcp;
        else if (http.Request.Headers.Origin.Count > 0) source = GovernanceSource.Screen;
        else source = GovernanceSource.Api;

        var machine = user.FindFirst(ExecutorClaims.WorkerId)?.Value;
        return new GovernanceCaller(new GovernanceActor(id, string.IsNullOrWhiteSpace(name) ? "Desconhecido" : name, roles, source), source,
            roles.FirstOrDefault(), machine);
    }
}
