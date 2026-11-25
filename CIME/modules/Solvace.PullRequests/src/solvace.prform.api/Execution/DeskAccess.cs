using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using solvace.prform.Governance;

namespace solvace.prform.Execution;

/// <summary>
/// 0099 (ajustes 2): papéis da Mesa de gestão. <c>management</c> = PO / gestor de problemas — Mesa, Base Solvace e
/// engenharia reversa, sem Bancada. "Perfil de desenvolvedor" = <c>user</c> ou <c>support</c>; quem tem <c>management</c> e
/// papel de dev tem os dois mundos.
/// </summary>
public static class DeskRoles
{
    public const string Admin = "admin";
    public const string Management = "management";

    /// <summary>Papéis que abrem a Mesa (REST <c>DevOps</c> e escrita de <c>Epics</c>).</summary>
    public const string DeskRolesCsv = "admin, management";

    private static readonly string[] DevRoles = ["user", "support"];

    /// <summary>Papéis do principal (claim longa <see cref="ClaimTypes.Role"/> ou curta <c>role</c>), sem repetir, minúsculos.</summary>
    public static IReadOnlyList<string> Of(ClaimsPrincipal user) =>
        user.FindAll(ClaimTypes.Role).Concat(user.FindAll("role"))
            .Select(c => c.Value?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(r => r.Length > 0)
            .Distinct()
            .ToList();

    public static bool HasDeskAccess(IEnumerable<string> roles) =>
        roles.Any(r => r.Equals(Admin, StringComparison.OrdinalIgnoreCase) || r.Equals(Management, StringComparison.OrdinalIgnoreCase));

    public static bool IsDeveloper(IEnumerable<string> roles) => roles.Any(r => DevRoles.Contains(r, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Perfil para os primeiros passos, o doctor e o especialista: <c>management</c> quando tem esse papel e nenhum de dev
    /// (nem admin); senão <c>dev</c> (inclusive sem papel conhecido — o comportamento de antes).
    /// </summary>
    public static string Profile(IEnumerable<string> roles)
    {
        var list = roles.ToList();
        return list.Any(r => r.Equals(Management, StringComparison.OrdinalIgnoreCase))
               && !IsDeveloper(list)
               && !list.Any(r => r.Equals(Admin, StringComparison.OrdinalIgnoreCase))
            ? ProfileManagement
            : ProfileDev;
    }

    public const string ProfileDev = "dev";
    public const string ProfileManagement = "management";

    /// <summary>
    /// Chamada automática que continua valendo como antes (ajustes 2): token do caso (especialista), credencial do executor
    /// ou skill rodando pelo executor/terminal (header <c>X-Execution-Client: skill</c> — o <c>gestao.sh</c> dos itens de
    /// gestão usa <c>DevOps/query</c> com a api-key de quem roda, que pode ser dev num item de card). O header qualquer um
    /// manda, então ele só vale para LEITURA (GET, <c>DevOps/query</c>, <c>DevOps/board/items</c>) — gravar na Mesa continua
    /// exigindo o papel ou um token do caso/executor.
    /// </summary>
    public static bool IsAutomation(HttpContext http) =>
        CaseClaims.RequestOf(http.User) is not null
        || ExecutorClaims.WorkerIdOf(http.User) is not null
        || (string.Equals(http.Request.Headers[GovernanceCaller.SkillHeader], "skill", StringComparison.OrdinalIgnoreCase)
            && IsReadCall(http.Request));

    /// <summary>Chamada de leitura da Mesa (o POST de consulta do DevOps também só lê).</summary>
    public static bool IsReadCall(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) return true;
        var path = request.Path.Value?.TrimEnd('/') ?? string.Empty;
        return HttpMethods.IsPost(request.Method)
               && (path.EndsWith("/DevOps/query", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("/DevOps/board/items", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A chamada alcança a Mesa? admin/management ou automação.</summary>
    public static bool CanUseDesk(HttpContext http) => IsAutomation(http) || HasDeskAccess(Of(http.User));
}

/// <summary>
/// 0099 (ajustes 2): exige <c>admin</c> ou <c>management</c> (ou automação — <see cref="DeskRoles.IsAutomation"/>). Sem
/// isso: 403 <c>{code:"forbidden", message, error}</c>. Autenticação continua com o <c>[Authorize]</c> do controller.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DeskAccessAttribute : Attribute, IAuthorizationFilter
{
    public const string Message = "A Mesa de gestão é só para os papéis admin ou management.";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true || DeskRoles.CanUseDesk(http))
            return;
        // Modal único do card: a Bancada abre o mesmo modal — quem trata o card (dev) usa os endpoints do card.
        if (context.ActionDescriptor.EndpointMetadata.OfType<CardAccessAttribute>().Any() && DeskRoles.IsDeveloper(DeskRoles.Of(http.User)))
            return;
        context.Result = new ObjectResult(new { code = "forbidden", message = Message, error = Message })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}

/// <summary>
/// 0099 (modal único do card): endpoint do CARD (ler, editar, comentar, anexos, histórico, mover no board e os metadados dos
/// editores) — vale também para o perfil de desenvolvedor (<c>user</c>/<c>support</c>), que abre o mesmo modal pela Bancada.
/// Criar card, board do épico e o resto da Mesa continuam só admin/management. Gravações seguem com a integração de quem age.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CardAccessAttribute : Attribute;
