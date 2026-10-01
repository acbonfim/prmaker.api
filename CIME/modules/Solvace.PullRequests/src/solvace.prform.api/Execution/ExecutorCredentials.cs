using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;

namespace solvace.prform.Execution;

/// <summary>Claims da credencial do executor (0039).</summary>
public static class ExecutorClaims
{
    public const string WorkerId = "executorId";
    public const string CredentialId = "executorCredential";

    public static Guid? WorkerIdOf(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(WorkerId)?.Value, out var id) ? id : null;
}

/// <summary>
/// Credencial do executor (0039): uma x-api-key (JWT assinado com <c>Auth:Secret</c>, o mesmo esquema validado pelo
/// <c>XApiKeyAuthenticationHandler</c>) com as claims do dono + o id do executor e o jti da credencial. Revogar o
/// executor (ou registrá-lo de novo) troca o jti e a credencial anterior para de valer.
/// </summary>
public class ExecutorTokenIssuer(IConfiguration configuration)
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        JwtRegisteredClaimNames.Jti, JwtRegisteredClaimNames.Exp, JwtRegisteredClaimNames.Nbf, JwtRegisteredClaimNames.Iat,
        JwtRegisteredClaimNames.Aud, JwtRegisteredClaimNames.Iss, ExecutorClaims.WorkerId, ExecutorClaims.CredentialId
    };

    public string Issue(ClaimsPrincipal owner, ExecutionWorker worker)
    {
        var secret = configuration["Auth:Secret"];
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException("Auth:Secret não configurado.");

        var claims = owner.Claims
            .Where(c => !Skip.Contains(c.Type))
            .Select(c => new Claim(c.Type, c.Value))
            .ToList();
        claims.Add(new Claim(ExecutorClaims.WorkerId, worker.Id.ToString()));
        claims.Add(new Claim(ExecutorClaims.CredentialId, worker.CredentialId.ToString()));
        claims.Add(new Claim(JwtRegisteredClaimNames.Jti, worker.CredentialId.ToString()));

        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret));
        var token = new JwtSecurityToken(claims: claims, signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// Credencial do executor só alcança o que o executor usa (fila, executores, plano, skills, MCP) e deixa de valer na
/// hora em que o executor é revogado (cache de 30 s).
/// </summary>
public partial class ExecutorCredentialMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    [GeneratedRegex(@"^/(api/v[\d.]+/(ExecutionQueue|ExecutionWorker|ExecutionPlan|Skills)(/|$)|mcp(/|$))", RegexOptions.IgnoreCase)]
    private static partial Regex AllowedPath();

    public async Task InvokeAsync(HttpContext context, IExecutionQueueApplication queue, IMemoryCache cache)
    {
        var workerId = ExecutorClaims.WorkerIdOf(context.User);
        if (workerId is null)
        {
            await next(context);
            return;
        }

        if (!AllowedPath().IsMatch(context.Request.Path.Value ?? string.Empty))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "A credencial do executor só acessa a fila, o plano e as skills." });
            return;
        }

        var credential = Guid.TryParse(context.User.FindFirst(ExecutorClaims.CredentialId)?.Value, out var c) ? c : Guid.Empty;
        var valid = await cache.GetOrCreateAsync($"execcred:{workerId}:{credential}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return queue.IsCredentialValidAsync(workerId.Value, credential, context.RequestAborted);
        });
        if (!valid)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Credencial do executor revogada ou substituída — rode: prmake-agent register" });
            return;
        }

        await next(context);
    }
}
