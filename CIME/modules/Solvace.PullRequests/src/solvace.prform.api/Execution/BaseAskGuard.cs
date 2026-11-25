using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using solvace.executionplans.application.Contracts;

namespace solvace.prform.Execution;

/// <summary>0102: claims do token da pergunta da Base Solvace pelo executor.</summary>
public static class BaseAskClaims
{
    public const string Request = "baseAskRequest";

    public static Guid? RequestOf(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirst(Request)?.Value, out var id) ? id : null;

    /// <summary>
    /// Ferramentas do PRMake que o Claude da pergunta usa: as de leitura da Base e a resposta. É tudo o que ele vê no
    /// <c>tools/list</c> (menos contexto, resposta mais rápida) e tudo o que ele pode chamar.
    /// </summary>
    public static readonly IReadOnlySet<string> Tools = new HashSet<string>(StringComparer.Ordinal)
    {
        "prmake_base_search", "prmake_base_get", "prmake_base_module", "prmake_base_impact", BaseAskMcpTools.AnswerTool
    };
}

/// <summary>
/// 0102: token da pergunta — as claims do dono (as da credencial do executor, sem as do executor) + o pedido. Emitido no claim
/// do pedido <c>base-ask</c>; vale só enquanto o pedido estiver ativo e só no MCP com <see cref="BaseAskClaims.Tools"/>
/// (<see cref="BaseAskMiddleware"/>).
/// </summary>
public class BaseAskTokenIssuer(IConfiguration configuration)
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        JwtRegisteredClaimNames.Jti, JwtRegisteredClaimNames.Exp, JwtRegisteredClaimNames.Nbf, JwtRegisteredClaimNames.Iat,
        JwtRegisteredClaimNames.Aud, JwtRegisteredClaimNames.Iss, ExecutorClaims.WorkerId, ExecutorClaims.CredentialId,
        CaseClaims.Card, CaseClaims.Request, CaseClaims.RequestedById, CaseClaims.RequestedByName, CaseClaims.OwnerExternalId,
        BaseAskClaims.Request
    };

    public string Issue(ClaimsPrincipal worker, Guid requestId)
    {
        var secret = configuration["Auth:Secret"];
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException("Auth:Secret não configurado.");
        var claims = worker.Claims
            .Where(c => !Skip.Contains(c.Type))
            .Select(c => new Claim(c.Type, c.Value))
            .ToList();
        claims.Add(new Claim(BaseAskClaims.Request, requestId.ToString()));
        claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret));
        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// 0102: o que o token da pergunta pode fazer — só o MCP (<c>/mcp</c>), só as <see cref="BaseAskClaims.Tools"/> e só enquanto
/// o pedido estiver ativo (cache de 15 s). Todo o resto: 403.
/// </summary>
public class BaseAskMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);

    public async Task InvokeAsync(HttpContext context, IExecutionQueueApplication queue, IMemoryCache cache)
    {
        if (BaseAskClaims.RequestOf(context.User) is not { } requestId)
        {
            await next(context);
            return;
        }
        var active = await cache.GetOrCreateAsync($"baseasktoken:{requestId}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return queue.IsBaseAskActiveAsync(requestId, context.RequestAborted);
        });
        if (!active)
        {
            await DenyAsync(context, StatusCodes.Status401Unauthorized, "O token da pergunta venceu (o pedido terminou).");
            return;
        }
        if (!context.Request.Path.StartsWithSegments("/mcp"))
        {
            await DenyAsync(context, StatusCodes.Status403Forbidden, "A pergunta da Base Solvace só usa as ferramentas prmake_base_* do MCP.");
            return;
        }
        if (HttpMethods.IsPost(context.Request.Method) && await DeniedToolAsync(context) is { } tool)
        {
            await DenyAsync(context, StatusCodes.Status403Forbidden,
                $"{tool} não está disponível na pergunta da Base Solvace — use prmake_base_search/get/module/impact e responda com {BaseAskMcpTools.AnswerTool}.");
            return;
        }
        await next(context);
    }

    /// <summary>Lista de ferramentas só com as da pergunta (o Claude não carrega as ~100 do PRMake no contexto).</summary>
    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> ListFilter(McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) =>
        async (context, ct) =>
        {
            var result = await next(context, ct);
            if (BaseAskClaims.RequestOf(context.User) is not null)
                result.Tools = result.Tools.Where(t => BaseAskClaims.Tools.Contains(t.Name)).ToList();
            return result;
        };

    private static async Task<string?> DeniedToolAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        JsonDocument? doc;
        try
        {
            doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        }
        catch (JsonException)
        {
            return "(corpo inválido)";
        }
        finally
        {
            context.Request.Body.Position = 0;
        }
        using (doc)
        {
            var calls = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
            foreach (var call in calls)
            {
                if (call.ValueKind != JsonValueKind.Object || !call.TryGetProperty("method", out var method) || method.GetString() != "tools/call")
                    continue;
                var name = call.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("name", out var n)
                    ? n.GetString() ?? string.Empty
                    : string.Empty;
                if (!BaseAskClaims.Tools.Contains(name))
                    return name.Length == 0 ? "(sem nome)" : name;
            }
        }
        return null;
    }

    private static async Task DenyAsync(HttpContext context, int status, string error)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error });
    }
}
