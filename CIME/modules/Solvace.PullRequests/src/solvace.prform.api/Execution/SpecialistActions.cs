using System.Security.Claims;
using ModelContextProtocol;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;
using solvace.prform.application.UserIntegrations;

namespace solvace.prform.Execution;

/// <summary>
/// r5 (1.6): ações que o Especialista do caso executa a pedido de uma pessoa (Timeline, horas, card no DevOps, PR, itens de
/// trabalho). Fora do token do caso não faz nada (o executor/skill usa as mesmas ferramentas). Com o token: confere o card,
/// devolve o texto "via Especialista do caso, a pedido de X", o ator das gravações e registra a ação concluída (ou falha)
/// como cartão na conversa. Integração pessoal ausente de quem pediu → erro claro (nada foi feito).
/// </summary>
public class SpecialistActions(CaseChatApplication chat, IHttpContextAccessor http, ILogger<SpecialistActions> logger)
{
    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    /// <summary>Quem chama é o especialista (token do caso).</summary>
    public bool IsSpecialist => CaseClaims.SpecialistOf(User) is not null;

    /// <summary>Nome de quem pediu na conversa (null fora do token do caso).</summary>
    public string? RequestedBy => IsSpecialist ? CaseClaims.RequestedByOf(User) ?? "Usuário" : null;

    /// <summary>"via Especialista do caso, a pedido de X" (null fora do token do caso).</summary>
    public string? Via => RequestedBy is { } who ? Text(who) : null;

    public static string Text(string requestedBy) => $"via {CaseChatApplication.SpecialistName}, a pedido de {requestedBy}";

    /// <summary>Texto com a marca do especialista no fim (inalterado fora do token do caso).</summary>
    public string Mark(string text, string separator = "\n\n") =>
        Via is { } via ? $"{text.TrimEnd()}{separator}_{via}._" : text;

    /// <summary>O especialista só age no card dele.</summary>
    public void EnsureCard(string card)
    {
        if (!IsSpecialist) return;
        var own = CaseClaims.CardOf(User)?.TrimStart('#');
        if (own is not null && !string.Equals(own, card.Trim().TrimStart('#'), StringComparison.Ordinal))
            throw new McpException($"O Especialista do caso só age no card dele ({own}).");
    }

    /// <summary>
    /// Roda a ação; com o token do caso, registra o cartão de ação na conversa (concluída com o resumo, ou falhou com o
    /// motivo). <paramref name="body"/> devolve a resposta da ferramenta e o resumo curto para o cartão.
    /// </summary>
    public async Task<string> RunAsync(string card, string title, string? detail, Func<Task<(string Response, string? Summary)>> body,
        CancellationToken ct)
    {
        if (!IsSpecialist)
            return (await body()).Response;
        EnsureCard(card);
        try
        {
            var (response, summary) = await body();
            await RecordAsync(card, title, detail, true, summary, ct);
            return response;
        }
        catch (Exception e) when (e is PersonalIntegrationRequiredException or InvalidOperationException && IsIntegrationError(e))
        {
            var message = $"{RequestedBy} (quem pediu) ainda não tem a integração pessoal configurada no PRMake: {e.Message}. " +
                          "Nada foi feito — configure em \"Minhas integrações\" ou peça para quem tem a integração.";
            await RecordAsync(card, title, detail, false, message, ct);
            throw new McpException(message, e);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await RecordAsync(card, title, detail, false, e.Message, ct);
            throw;
        }
    }

    private static bool IsIntegrationError(Exception e) =>
        e is PersonalIntegrationRequiredException || e.Message.Contains("Plugin", StringComparison.OrdinalIgnoreCase);

    private async Task RecordAsync(string card, string title, string? detail, bool ok, string? result, CancellationToken ct)
    {
        if (CaseClaims.SpecialistOf(User) is not { } actor) return;
        try
        {
            await chat.RecordActionAsync(card, title, detail, ok, result, RequestedBy ?? "Usuário", actor, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Ação do especialista no card {Card} feita, mas o cartão da conversa não foi registrado", card);
        }
    }

    /// <summary>Ator das gravações do especialista: quem pediu, com a marca do especialista.</summary>
    public ExecutionActor? Actor()
    {
        if (!IsSpecialist) return null;
        Guid? id = Guid.TryParse(User.FindFirst(CaseClaims.RequestedById)?.Value, out var g) && g != Guid.Empty ? g : null;
        return new ExecutionActor(id, $"{CaseChatApplication.SpecialistName}, a pedido de {RequestedBy}", false);
    }
}
