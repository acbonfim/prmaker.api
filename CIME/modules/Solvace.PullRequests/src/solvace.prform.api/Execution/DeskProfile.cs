using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.Execution;

/// <summary>
/// 0099 (ajustes 2): perfil de quem pede (papéis do Cime.Auth) para o especialista e os itens de gestão — "PO / gestor de
/// problemas" (management sem papel de dev) não usa git/gh/código/banco do cliente/AWS CLI. Os papéis vêm do schema
/// <c>auth</c> (leitura) pelo ExternalId — o token do caso carrega os papéis do DONO do executor, não os de quem pediu.
/// </summary>
public sealed class DeskProfileService(AuthenticationContext auth, IMemoryCache cache, ILogger<DeskProfileService> logger)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    /// <summary>Papéis do usuário (minúsculos); usuário desconhecido ou banco fora → vazio.</summary>
    public async Task<IReadOnlyList<string>> RolesAsync(Guid externalId, CancellationToken ct)
    {
        if (externalId == Guid.Empty) return [];
        return await cache.GetOrCreateAsync($"desk-roles:{externalId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            try
            {
                var roles = await auth.Database.SqlQuery<string>($"""
                    SELECT r."Name" AS "Value"
                    FROM auth."AspNetUsers" u
                    JOIN auth."AspNetUserRoles" ur ON ur."UserId" = u."Id"
                    JOIN auth."AspNetRoles" r ON r."Id" = ur."RoleId"
                    WHERE u."ExternalId" = {externalId}
                    """).ToListAsync(ct);
                return (IReadOnlyList<string>)roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim().ToLowerInvariant()).Distinct().ToList();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                logger.LogWarning(e, "Papéis do usuário {User} indisponíveis", externalId);
                return [];
            }
        }) ?? [];
    }

    /// <summary>
    /// Quem pediu e os papéis: com o token do caso, a pessoa que pediu a resposta (papéis lidos do banco — o token traz os do
    /// dono da máquina); senão quem chama (papéis da api-key).
    /// </summary>
    public async Task<(string? Name, IReadOnlyList<string> Roles)> RequesterAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (CaseClaims.SpecialistOf(user) is not null
            && Guid.TryParse(user.FindFirst(CaseClaims.RequestedById)?.Value, out var requester) && requester != Guid.Empty)
        {
            var owner = Guid.TryParse(user.FindFirst(CaseClaims.OwnerExternalId)?.Value ?? user.FindFirst("ExternalId")?.Value, out var o) ? o : Guid.Empty;
            var roles = requester == owner ? DeskRoles.Of(user) : [];
            if (roles.Count == 0) roles = await RolesAsync(requester, ct);
            return (CaseClaims.RequestedByOf(user), roles);
        }
        return (user.FindFirst(ClaimTypes.Name)?.Value, DeskRoles.Of(user));
    }

    /// <summary>Seção "Perfil de quem pediu" para o contexto/regras de um épico.</summary>
    public async Task<string> MarkdownAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var (name, roles) = await RequesterAsync(user, ct);
        return DeskPersona.Markdown(name, roles);
    }
}

/// <summary>0099 (ajustes 2): texto da persona por perfil (contexto do especialista de gestão e regras do épico).</summary>
public static class DeskPersona
{
    public const string Heading = "## Perfil de quem pediu";

    public static string Markdown(string? name, IReadOnlyList<string> roles)
    {
        var profile = DeskRoles.Profile(roles);
        var sb = new StringBuilder();
        sb.AppendLine().AppendLine(Heading);
        var who = string.IsNullOrWhiteSpace(name) ? "Quem pediu" : name.Trim();
        var list = roles.Count == 0 ? "nenhum papel conhecido" : string.Join(", ", roles);
        if (profile == DeskRoles.ProfileManagement)
        {
            sb.AppendLine($"- {who} · papéis: {list} → **perfil `management` (PO / gestor de problemas, sem papel de desenvolvedor)**.");
            sb.AppendLine("- A máquina dessa pessoa pode não ter código, GitHub, banco do cliente nem AWS. **Não use** git, gh, leitura/edição de " +
                          "código dos repositórios, consultas ao banco do cliente (sql-query) nem a CLI da AWS — nem para \"confirmar\".");
            sb.AppendLine("- **Use**: Base Solvace (`prmake_base_search` → `prmake_base_get` → `prmake_base_impact`) e a engenharia reversa; " +
                          "ferramentas do PRMake (épico, cards do DevOps, itens de trabalho, histórias, PDF); acompanhamento das entregas com " +
                          "`prmake_epic_monitor(epic)` (PRs do registro do PRMake, estado/coluna e ambientes); retrato de deploy já gravado com " +
                          "`prmake_aws_status(card)` (sem retrato: peça a coleta na aba AWS do card a quem tem a CLI); validar no ambiente e " +
                          "reproduzir bug pelo navegador (`validar-ambiente` — senha guardada na máquina).");
            sb.AppendLine("- Responda como para um PO: o que está pronto, onde está (PR mesclado, ambiente), o que falta e quem precisa agir — " +
                          "sem jargão de código; quando a resposta exigir código ou banco, diga isso e proponha um item de trabalho para um dev.");
        }
        else
        {
            sb.AppendLine($"- {who} · papéis: {list} → **perfil `dev`**.");
            sb.AppendLine("- Além das ferramentas do PRMake e da Base Solvace, pode consultar código (somente leitura), git/gh e o banco (somente " +
                          "leitura) quando a pergunta pedir; gravações continuam só pelo PRMake.");
        }
        return sb.ToString();
    }
}
