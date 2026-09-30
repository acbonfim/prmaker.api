using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Responses;

namespace solvace.prform.Controllers;

/// <summary>
/// Cópia filtrada do Knowledge Center (feature 0033). A skill sincroniza (quem tem a credencial do banco do KC); as
/// análises consultam pelo espelho local ou por aqui.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class KnowledgeController(IKnowledgeApplication application, solvace.timeline.application.Contracts.IUserRepository users) : ControllerBase
{
    /// <summary>Marca d'água e contagem do ambiente (padrão: o ativo no plugin).</summary>
    [HttpGet("state")]
    public Task<ActionResult<KnowledgeStateResponse>> State([FromQuery] string? environment, CancellationToken ct) =>
        Run<KnowledgeStateResponse>(async () => Ok(await application.GetStateAsync(environment, ct)));

    /// <summary>Lote da sincronização (o backend reaplica o piso de filtro de teste).</summary>
    [HttpPost("sync")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public Task<ActionResult<KnowledgeSyncResponse>> Sync([FromBody] KnowledgeSyncRequest request, CancellationToken ct) =>
        Run<KnowledgeSyncResponse>(async () => Ok(await application.SyncAsync(request, await ActorAsync(users, User, ct), ct)));

    /// <summary>Busca nos artigos do ambiente ativo (trecho do texto).</summary>
    [HttpGet("articles")]
    public Task<ActionResult<List<KnowledgeArticleResponse>>> Search([FromQuery] string? q, [FromQuery] int limit = 10, CancellationToken ct = default) =>
        Run<List<KnowledgeArticleResponse>>(async () => Ok(await application.SearchAsync(q, limit, ct)));

    [HttpGet("articles/{number:int}")]
    public Task<ActionResult<KnowledgeArticleResponse>> Article([FromRoute] int number, CancellationToken ct) =>
        Run<KnowledgeArticleResponse>(async () => Ok(await application.GetArticleAsync(number, ct)));

    internal static async Task<string> ActorAsync(solvace.timeline.application.Contracts.IUserRepository users, ClaimsPrincipal user, CancellationToken ct)
    {
        string? name = null;
        if (Guid.TryParse(user.FindFirst("ExternalId")?.Value, out var id))
            name = await users.GetFullNameAsync(id, ct);
        name ??= user.FindFirst(ClaimTypes.Name)?.Value;
        return string.IsNullOrWhiteSpace(name) ? "Usuário" : name.Trim();
    }

    private async Task<ActionResult<T>> Run<T>(Func<Task<ActionResult>> action)
    {
        try { return await action(); }
        catch (DomainException e) { return BadRequest(new { error = e.Message }); }
        catch (KnowledgeNotFoundException e) { return NotFound(new { error = e.Message }); }
    }
}
