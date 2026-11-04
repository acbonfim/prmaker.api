using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;

namespace solvace.prform.Execution;

/// <summary>0080: claims do token do Especialista do caso.</summary>
public static class CaseClaims
{
    public const string Card = "caseCard";
    public const string Request = "caseRequest";

    public static string? CardOf(ClaimsPrincipal user) => user.FindFirst(Card)?.Value;

    public static Guid? RequestOf(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(Request)?.Value, out var id) ? id : null;

    /// <summary>Quem chama é o especialista (token do caso) — com o card e o pedido dele.</summary>
    public static CaseActor? SpecialistOf(ClaimsPrincipal user) =>
        RequestOf(user) is { } request
            ? new CaseActor(Guid.TryParse(user.FindFirst("ExternalId")?.Value, out var id) ? id : null, CaseChatApplication.SpecialistName, false, request)
            : null;
}

/// <summary>
/// 0080: token do Especialista do caso — as claims do dono (as mesmas da credencial do executor, sem as do executor) +
/// o card e o pedido. Emitido no claim do pedido do especialista; vale só enquanto o pedido estiver ativo
/// (<see cref="CaseSpecialistMiddleware"/>) e só lê ou conversa: nunca altera o plano.
/// </summary>
public class CaseTokenIssuer(IConfiguration configuration)
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        JwtRegisteredClaimNames.Jti, JwtRegisteredClaimNames.Exp, JwtRegisteredClaimNames.Nbf, JwtRegisteredClaimNames.Iat,
        JwtRegisteredClaimNames.Aud, JwtRegisteredClaimNames.Iss, ExecutorClaims.WorkerId, ExecutorClaims.CredentialId,
        CaseClaims.Card, CaseClaims.Request
    };

    public string Issue(ClaimsPrincipal worker, Guid requestId, string cardNumber)
    {
        var secret = configuration["Auth:Secret"];
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException("Auth:Secret não configurado.");
        var claims = worker.Claims
            .Where(c => !Skip.Contains(c.Type))
            .Select(c => new Claim(c.Type, c.Value))
            .ToList();
        claims.Add(new Claim(CaseClaims.Card, cardNumber));
        claims.Add(new Claim(CaseClaims.Request, requestId.ToString()));
        claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secret));
        var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(6),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>
/// 0080: o Especialista do caso NÃO altera o plano — garantido aqui, não na skill. Com o token do caso:
/// <list type="bullet">
/// <item>só vale enquanto o pedido do especialista do card estiver ativo (cache de 15 s);</item>
/// <item>REST: <c>GET</c> em qualquer lugar (sem o header da skill, que marca comentários como lidos) e qualquer método
/// em <c>CaseChat/{card}</c> do próprio card;</item>
/// <item>MCP: só as ferramentas de leitura e as <c>prmake_case_*</c> (confere o corpo JSON-RPC <c>tools/call</c>);</item>
/// <item>todo o resto: 403.</item>
/// </list>
/// </summary>
public partial class CaseSpecialistMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);

    /// <summary>Ferramentas MCP que o especialista pode chamar (leitura + conversa do caso).</summary>
    public static readonly IReadOnlySet<string> AllowedTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "prmake_plan", "prmake_answers", "prmake_attachment", "prmake_devops_config", "prmake_config", "prmake_card", "prmake_queue",
        "prmake_base_search", "prmake_base_get", "prmake_base_module", "prmake_base_impact",
        "prmake_case_context", "prmake_case_messages", "prmake_case_reply", "prmake_case_point", "prmake_case_propose",
        "prmake_case_action", "prmake_case_action_done", "prmake_case_file"
    };

    [GeneratedRegex(@"^/api/v[\d.]+/CaseChat/(?<card>[^/]+)(/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex CaseChatPath();

    public async Task InvokeAsync(HttpContext context, IExecutionQueueApplication queue, IMemoryCache cache)
    {
        var requestId = CaseClaims.RequestOf(context.User);
        if (requestId is null)
        {
            await next(context);
            return;
        }
        var card = CaseClaims.CardOf(context.User) ?? string.Empty;

        var active = await cache.GetOrCreateAsync($"casetoken:{requestId}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return queue.IsSpecialistRequestActiveAsync(requestId.Value, card, context.RequestAborted);
        });
        if (!active)
        {
            await DenyAsync(context, StatusCodes.Status401Unauthorized, "O token do Especialista do caso venceu (a resposta terminou).");
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;
        // O header da skill marca comentários/arquivos do plano como lidos — o especialista não mexe nisso.
        context.Request.Headers.Remove("X-Execution-Client");

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
        {
            await next(context);
            return;
        }

        if (CaseChatPath().Match(path) is { Success: true } m)
        {
            if (!string.Equals(Uri.UnescapeDataString(m.Groups["card"].Value).TrimStart('#'), card, StringComparison.Ordinal))
            {
                await DenyAsync(context, StatusCodes.Status403Forbidden, "O especialista só conversa no card dele.");
                return;
            }
            await next(context);
            return;
        }

        if (path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(method))
        {
            if (await DeniedToolAsync(context) is { } tool)
            {
                await DenyAsync(context, StatusCodes.Status403Forbidden,
                    $"O Especialista do caso não altera o plano ({tool} recusada). Proponha a mudança com prmake_case_propose ou peça confirmação com prmake_case_action.");
                return;
            }
            await next(context);
            return;
        }

        await DenyAsync(context, StatusCodes.Status403Forbidden,
            "O Especialista do caso só lê e conversa — o plano muda só quando uma pessoa aceita a proposta.");
    }

    /// <summary>Nome da ferramenta MCP chamada fora da lista (null = todas permitidas ou não é chamada de ferramenta).</summary>
    private static async Task<string?> DeniedToolAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        using var doc = await TryParseAsync(context.Request.Body, context.RequestAborted);
        context.Request.Body.Position = 0;
        if (doc is null) return "(corpo inválido)";
        var calls = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        foreach (var call in calls)
        {
            if (call.ValueKind != JsonValueKind.Object || !call.TryGetProperty("method", out var method) || method.GetString() != "tools/call")
                continue;
            var name = call.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("name", out var n)
                ? n.GetString() ?? string.Empty
                : string.Empty;
            if (!AllowedTools.Contains(name))
                return name.Length == 0 ? "(sem nome)" : name;
        }
        return null;
    }

    private static async Task<JsonDocument?> TryParseAsync(Stream body, CancellationToken ct)
    {
        try
        {
            return await JsonDocument.ParseAsync(body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task DenyAsync(HttpContext context, int status, string error)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error });
    }
}
