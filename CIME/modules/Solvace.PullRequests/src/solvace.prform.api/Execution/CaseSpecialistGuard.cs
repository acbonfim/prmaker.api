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
/// <item>MCP: só as ferramentas de leitura, as <c>prmake_case_*</c>, (0085) criar/editar itens de trabalho e (r5) as AÇÕES que
/// uma pessoa pede na conversa — Timeline, ações do card no DevOps (estado, horas...), abrir PR, andamento/cancelar/executar
/// item de trabalho (confere o corpo JSON-RPC <c>tools/call</c>) e (0096) as mudanças do PLANO ATUAL pedidas pela pessoa —
/// etapas, espera, situação publicada, respostas, arquivos, links, evidências, status (<see cref="SpecialistPlanEdits"/>
/// pausa/retoma o tratamento e registra o cartão). Ficam recusadas as do tratamento: versão nova (pela proposta aceita),
/// perguntas novas, travas, andamento interno e ler os comentários do plano;</item>
/// <item>todo o resto: 403.</item>
/// </list>
/// r5: as ações (<see cref="ActingTools"/>) rodam com a identidade de quem pediu na conversa (claim <c>ExternalId</c> trocada
/// pela de quem pediu — integrações pessoais dela), com <see cref="CaseClaims.RequestedByName"/> para o texto "via
/// Especialista do caso, a pedido de ...".
/// </summary>
public partial class CaseSpecialistMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);

    /// <summary>Ferramentas MCP que o especialista pode chamar (leitura + conversa do caso + itens de trabalho pending).</summary>
    public static readonly IReadOnlySet<string> AllowedTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "prmake_plan", "prmake_answers", "prmake_attachment", "prmake_devops_config", "prmake_config", "prmake_card", "prmake_queue",
        "prmake_base_search", "prmake_base_get", "prmake_base_module", "prmake_base_impact",
        "prmake_case_context", "prmake_case_messages", "prmake_case_reply", "prmake_case_point", "prmake_case_propose",
        "prmake_case_action", "prmake_case_action_done", "prmake_case_file",
        // 0085: regras do card e itens de trabalho (criar/editar itens pending; executar sub-tarefa não).
        "prmake_rules", "prmake_work_items", "prmake_work_item", "prmake_work_item_create", "prmake_work_item_update",
        // Sub-tarefa a pedido no item que espelha o plano: só ACRESCENTA uma etapa pendente (as outras não mudam).
        "prmake_work_item_add_task",
        // Item avulso já começado/fechado: reabrir (refazendo sub-tarefas) e editar sub-tarefa pendente — nunca o plano.
        "prmake_work_item_reopen", "prmake_work_task_update",
        // 0085 r4: ferramentas (modelos) do card — listar e criar o item a partir de uma.
        "prmake_tools", "prmake_tool_run",
        // r5: ações pedidas na conversa (nunca o plano): Timeline, card no DevOps (estado, horas, ações), PR, itens.
        "prmake_timeline", "prmake_devops", "prmake_pr_open",
        "prmake_work_task", "prmake_work_item_cancel", "prmake_work_item_execute",
        // Card 77988: itens a partir da análise respondida (o Claude do tratamento monta o plano — o especialista não mexe nele).
        "prmake_analysis_items",
        // r5 (B5a): arquivos do item de trabalho (o chamado completo, script do item) — nunca os arquivos do plano (prmake_file).
        "prmake_work_item_file", "prmake_work_item_files",
        // 0092: item de validação em ambiente (uma sub-tarefa por ambiente, prints como evidência).
        "prmake_env_validation",
        // 0093: item de reprodução do bug no ambiente (por padrão o do cliente).
        "prmake_env_reproduction",
        // 0091/0089: entregas e evidências do item de trabalho.
        "prmake_work_deliverable", "prmake_work_item_evidence",
        // 0096: guardião do caso — muda o plano atual a pedido da pessoa (SpecialistPlanEdits: pausa/retoma o tratamento,
        // cartão da ação). Ficam só com o tratamento: versão nova (prmake_correction, pela proposta aceita), perguntas novas e
        // travas (prmake_ask/prmake_block), o andamento interno (prmake_checkpoint/prmake_control) e ler comentários do
        // plano (prmake_notes marca como lidos — o tratamento perderia a instrução); prmake_steps reescreve a lista inteira (as
        // pendentes que ficam de fora somem) — o especialista ajusta UMA etapa com prmake_step (title/description).
        "prmake_situation", "prmake_step", "prmake_advance", "prmake_unblock", "prmake_plan_status",
        "prmake_answer", "prmake_cancel_questions", "prmake_handoff", "prmake_situation_publish", "prmake_file", "prmake_link",
        "prmake_evidence", "prmake_log", "prmake_code_ref"
    };

    /// <summary>
    /// r5: ferramentas que gravam fora da conversa a pedido de uma pessoa — rodam como quem pediu (integrações dela) e viram
    /// cartão de ação concluída na conversa.
    /// </summary>
    public static readonly IReadOnlySet<string> ActingTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "prmake_timeline", "prmake_devops", "prmake_pr_open",
        "prmake_work_item_create", "prmake_work_item_update", "prmake_work_item_add_task", "prmake_work_item_reopen",
        "prmake_work_task_update", "prmake_tool_run",
        "prmake_work_task", "prmake_work_item_cancel", "prmake_work_item_execute", "prmake_analysis_items", "prmake_env_validation", "prmake_env_reproduction",
        "prmake_work_deliverable", "prmake_work_item_evidence",
        // 0096: mudanças do plano a pedido da pessoa.
        "prmake_step", "prmake_advance", "prmake_unblock", "prmake_plan_status", "prmake_answer", "prmake_cancel_questions",
        "prmake_handoff", "prmake_situation_publish", "prmake_file", "prmake_link", "prmake_evidence", "prmake_log", "prmake_code_ref"
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
            var (denied, acting) = await InspectToolsAsync(context);
            if (denied is { } tool)
            {
                await DenyAsync(context, StatusCodes.Status403Forbidden,
                    $"{tool} fica com o Claude do tratamento (versão nova do plano, perguntas novas, travas, andamento interno). " +
                    "Ajuste o plano atual com prmake_step (texto/status de uma etapa)/prmake_advance/prmake_handoff/prmake_situation_publish; versão nova " +
                    "(a solução mudou) com prmake_case_propose.");
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

    /// <summary>A ferramenta passa com o token do caso?</summary>
    public static bool IsAllowed(string tool) => AllowedTools.Contains(tool);

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
