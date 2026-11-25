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
    /// <summary>r5: quem pediu a resposta (posto pelo <see cref="CaseSpecialistMiddleware"/> a cada chamada).</summary>
    public const string RequestedById = "caseRequestedById";
    public const string RequestedByName = "caseRequestedBy";
    /// <summary>r5: ExternalId do dono da máquina (o token nasce com ele) quando a chamada passa a agir como quem pediu.</summary>
    public const string OwnerExternalId = "caseOwnerExternalId";

    public static string? CardOf(ClaimsPrincipal user) => user.FindFirst(Card)?.Value;

    public static Guid? RequestOf(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(Request)?.Value, out var id) ? id : null;

    /// <summary>r5: nome de quem pediu a resposta ao especialista (null fora do token do caso).</summary>
    public static string? RequestedByOf(ClaimsPrincipal user) =>
        user.FindFirst(RequestedByName)?.Value is { Length: > 0 } name ? name : null;

    /// <summary>Quem chama é o especialista (token do caso) — com o card e o pedido dele.</summary>
    public static CaseActor? SpecialistOf(ClaimsPrincipal user) =>
        RequestOf(user) is { } request
            ? new CaseActor(Guid.TryParse(user.FindFirst("ExternalId")?.Value, out var id) ? id : null, CaseChatApplication.SpecialistName, false, request)
            : null;
}

/// <summary>
/// 0080: token do Especialista do caso — as claims do dono (as mesmas da credencial do executor, sem as do executor) +
/// o card e o pedido. Emitido no claim do pedido do especialista; vale só enquanto o pedido estiver ativo
/// (<see cref="CaseSpecialistMiddleware"/>) e só usa o que o middleware deixa (0096: inclusive mudar o plano atual a pedido).
/// </summary>
public class CaseTokenIssuer(IConfiguration configuration)
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        JwtRegisteredClaimNames.Jti, JwtRegisteredClaimNames.Exp, JwtRegisteredClaimNames.Nbf, JwtRegisteredClaimNames.Iat,
        JwtRegisteredClaimNames.Aud, JwtRegisteredClaimNames.Iss, ExecutorClaims.WorkerId, ExecutorClaims.CredentialId,
        CaseClaims.Card, CaseClaims.Request, CaseClaims.RequestedById, CaseClaims.RequestedByName, CaseClaims.OwnerExternalId
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
/// 0080: o que o Especialista do caso pode fazer — garantido aqui, não na skill. Com o token do caso:
/// <list type="bullet">
/// <item>só vale enquanto o pedido do especialista do card estiver ativo (cache de 15 s);</item>
/// <item>REST: <c>GET</c> em qualquer lugar (sem o header da skill, que marca comentários como lidos) e qualquer método
/// em <c>CaseChat/{card}</c> do próprio card;</item>
/// <item>MCP (confere o corpo JSON-RPC <c>tools/call</c>): todas as ferramentas do PRMake — leitura, conversa, itens de
/// trabalho (inclusive de outro card do caso), as AÇÕES que uma pessoa pede na conversa (Timeline, DevOps, PR, itens) e (0096)
/// as mudanças do PLANO ATUAL (<see cref="SpecialistPlanEdits"/> pausa/retoma o tratamento e registra o cartão) —, menos as
/// do tratamento (<see cref="TreatmentOnlyTools"/>);</item>
/// <item>todo o resto: 403.</item>
/// </list>
/// r5: as ações (<see cref="ActingTools"/>) rodam com a identidade de quem pediu na conversa (claim <c>ExternalId</c> trocada
/// pela de quem pediu — integrações pessoais dela), com <see cref="CaseClaims.RequestedByName"/> para o texto "via
/// Especialista do caso, a pedido de ...".
/// </summary>
public partial class CaseSpecialistMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Card 78505: o especialista é o guardião do caso — usa TODAS as ferramentas do PRMake (leitura, conversa, itens de
    /// trabalho de qualquer card do caso, ações no DevOps/GitHub/Timeline, ajustes do plano atual), menos as que montam o
    /// tratamento: versão/etapas do plano inteiras (<c>prmake_correction</c>, <c>prmake_steps</c> — reescreve a lista e some
    /// com as pendentes de fora), perguntas novas e travas (<c>prmake_ask</c>/<c>prmake_block</c>), o andamento interno
    /// (<c>prmake_checkpoint</c>/<c>prmake_control</c>) e ler os comentários do plano (<c>prmake_notes</c> marca como lidos —
    /// o tratamento perderia a instrução). Ferramenta nova já nasce liberada.
    /// </summary>
    public static readonly IReadOnlySet<string> TreatmentOnlyTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "prmake_correction", "prmake_steps", "prmake_ask", "prmake_block", "prmake_checkpoint", "prmake_control", "prmake_notes",
        // 0102: resposta da pergunta da Base Solvace pelo executor — só com o token da pergunta.
        "prmake_base_answer"
    };

    /// <summary>
    /// r5: ferramentas que gravam fora da conversa a pedido de uma pessoa — rodam como quem pediu (integrações dela) e viram
    /// cartão de ação concluída na conversa.
    /// </summary>
    public static readonly IReadOnlySet<string> ActingTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "prmake_timeline", "prmake_devops", "prmake_devops_spec", "prmake_pr_open",
        "prmake_work_item_create", "prmake_work_item_update", "prmake_work_item_add_task", "prmake_work_task_redo", "prmake_work_item_reopen",
        "prmake_work_task_update", "prmake_work_task_move", "prmake_work_task_control", "prmake_tool_run",
        "prmake_work_task", "prmake_work_item_cancel", "prmake_work_item_execute", "prmake_analysis_items", "prmake_env_validation", "prmake_env_reproduction",
        "prmake_work_deliverable", "prmake_work_item_evidence",
        // 0096: mudanças do plano a pedido da pessoa.
        "prmake_step", "prmake_advance", "prmake_unblock", "prmake_plan_status", "prmake_answer", "prmake_cancel_questions",
        "prmake_handoff", "prmake_situation_publish", "prmake_file", "prmake_link", "prmake_evidence", "prmake_log", "prmake_code_ref",
        // 0099: Mesa de gestão — épico, cards do épico e PDF nos arquivos do item.
        "prmake_epic_save", "prmake_epic_cards", "prmake_pdf",
        // 0099: cards do DevOps pela Mesa de gestão (épico: qualquer card; card: só ele — DevOpsMcpTools.CaseScopeError).
        "prmake_devops_card_create", "prmake_devops_card_update", "prmake_devops_card_move",
        // 0099 (ajustes 2): histórias/bugs do épico (rascunho como quem pediu; conversão com a integração do DevOps de quem pediu).
        "prmake_epic_story_save", "prmake_epic_story_convert"
    };

    /// <summary>
    /// 0099: REST que grava e o token do caso pode chamar — arquivo/PDF de um item (o controller confere que o item é do card
    /// ou épico do caso) e gerar PDF para baixar (não grava nada).
    /// </summary>
    [GeneratedRegex(@"^/api/v[\d.]+/(WorkItems/\d+/files(/pdf)?|Pdf/render)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex CaseWritablePath();

    [GeneratedRegex(@"^/api/v[\d.]+/CaseChat/(?<card>[^/]+)(/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex CaseChatPath();

    [GeneratedRegex(@"^/api/v[\d.]+/Governance/(evaluate|hooks/events|standards/check)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex GovernanceHookPath();

    public async Task InvokeAsync(HttpContext context, IExecutionQueueApplication queue, IMemoryCache cache)
    {
        var requestId = CaseClaims.RequestOf(context.User);
        if (requestId is null)
        {
            await next(context);
            return;
        }
        var card = CaseClaims.CardOf(context.User) ?? string.Empty;

        var requester = await cache.GetOrCreateAsync($"casetoken:{requestId}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return queue.GetActiveSpecialistRequesterAsync(requestId.Value, card, context.RequestAborted);
        });
        if (requester is null)
        {
            await DenyAsync(context, StatusCodes.Status401Unauthorized, "O token do Especialista do caso venceu (a resposta terminou).");
            return;
        }
        context.User = WithRequester(context.User, requester, actAsRequester: false);

        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;
        // O header da skill marca comentários/arquivos do plano como lidos — o especialista não mexe nisso.
        context.Request.Headers.Remove("X-Execution-Client");

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
        {
            await next(context);
            return;
        }

        // 0098: os hooks de governança do especialista consultam as regras e mandam os eventos (só leitura/registro).
        if (HttpMethods.IsPost(method) && GovernanceHookPath().IsMatch(path))
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

        if (HttpMethods.IsPost(method) && CaseWritablePath().IsMatch(path))
        {
            context.User = WithRequester(context.User, requester, actAsRequester: true);
            await next(context);
            return;
        }

        if (path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(method))
        {
            var (denied, acting) = await InspectToolsAsync(context);
            if (denied is { } tool)
            {
                await DenyAsync(context, StatusCodes.Status403Forbidden,
                    $"{tool} fica com o Claude do tratamento (montar as etapas do plano, perguntas novas, travas, andamento interno). " +
                    "Ajuste o plano atual com prmake_step (texto/status de uma etapa)/prmake_advance/prmake_handoff/prmake_situation_publish; a solução " +
                    "mudou: prmake_case_propose (ajusta o mesmo plano — não existe versão nova); outra entrega: prmake_work_item_create.");
                return;
            }
            if (acting)
                context.User = WithRequester(context.User, requester, actAsRequester: true);
            await next(context);
            return;
        }

        await DenyAsync(context, StatusCodes.Status403Forbidden,
            "O Especialista do caso usa o REST só para ler e para a conversa do card — o resto vai pelas ferramentas do PRMake (MCP).");
    }

    /// <summary>
    /// Ferramenta MCP chamada fora da lista (null = todas permitidas ou não é chamada de ferramenta) e se alguma chamada é
    /// uma ação (<see cref="ActingTools"/>).
    /// </summary>
    private static async Task<(string? Denied, bool Acting)> InspectToolsAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        using var doc = await TryParseAsync(context.Request.Body, context.RequestAborted);
        context.Request.Body.Position = 0;
        if (doc is null) return ("(corpo inválido)", false);
        var calls = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        var acting = false;
        foreach (var call in calls)
        {
            if (call.ValueKind != JsonValueKind.Object || !call.TryGetProperty("method", out var method) || method.GetString() != "tools/call")
                continue;
            var name = call.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("name", out var n)
                ? n.GetString() ?? string.Empty
                : string.Empty;
            if (!IsAllowed(name))
                return (name.Length == 0 ? "(sem nome)" : name, false);
            acting |= ActingTools.Contains(name);
        }
        return (null, acting);
    }

    /// <summary>A ferramenta passa com o token do caso? (todas, menos as do tratamento)</summary>
    public static bool IsAllowed(string tool) => tool.Length > 0 && !TreatmentOnlyTools.Contains(tool);

    /// <summary>
    /// r5: claims de quem pediu (nome/id) e, com <paramref name="actAsRequester"/>, o <c>ExternalId</c> trocado pelo de quem
    /// pediu — as integrações pessoais (DevOps, GitHub) e o autor na Timeline passam a ser dessa pessoa. Sem pessoa (pedido
    /// do sistema), fica o dono da máquina.
    /// </summary>
    public static ClaimsPrincipal WithRequester(ClaimsPrincipal user, SpecialistRequester requester, bool actAsRequester)
    {
        var claims = user.Claims
            .Where(c => c.Type is not (CaseClaims.RequestedById or CaseClaims.RequestedByName))
            .Select(c => new Claim(c.Type, c.Value))
            .ToList();
        var name = string.IsNullOrWhiteSpace(requester.Name) ? requester.OwnerName : requester.Name.Trim();
        var id = requester.UserId is { } u && u != Guid.Empty ? u : requester.OwnerUserId;
        claims.Add(new Claim(CaseClaims.RequestedByName, name));
        claims.Add(new Claim(CaseClaims.RequestedById, id.ToString()));
        if (actAsRequester && claims.FirstOrDefault(c => c.Type == "ExternalId") is { } external && external.Value != id.ToString())
        {
            claims.Remove(external);
            claims.RemoveAll(c => c.Type == CaseClaims.OwnerExternalId);
            claims.Add(new Claim(CaseClaims.OwnerExternalId, external.Value));
            claims.Add(new Claim("ExternalId", id.ToString()));
        }
        var identity = user.Identity as ClaimsIdentity;
        return new ClaimsPrincipal(new ClaimsIdentity(claims, identity?.AuthenticationType ?? "case", identity?.NameClaimType ?? ClaimTypes.Name,
            identity?.RoleClaimType ?? ClaimTypes.Role));
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
