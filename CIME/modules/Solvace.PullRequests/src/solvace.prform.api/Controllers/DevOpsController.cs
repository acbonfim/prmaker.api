using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.azure.application.Contract;
using solvace.azure.domain.Exceptions;
using solvace.azure.domain.Models;
using solvace.prform.Execution;

namespace solvace.prform.Controllers;

/// <summary>
/// 0099 (Mesa de gestão): o Azure DevOps na tela — metadados para os editores (tipos, campos com picklist, tags,
/// áreas/iterações, pessoas, times, boards), o board "igual ao DevOps" (configuração com <c>configHash</c> + cartões),
/// consultas e criar/editar/mover/comentar cards. Tudo com a integração pessoal de quem age ("AzureDevOps Configurations").
/// Gravações: Timeline do card + <c>devopsCardsUpdated</c> no grupo <c>devops</c> (<see cref="DevOpsDeskRunner"/>).
/// Erros: 400/403/404/409/502 <c>{code, message}</c> com a mensagem do DevOps quando foi ele que recusou.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class DevOpsController(IDevOpsDeskService devops, DevOpsDeskRunner runner, IEpicCardSource epicCards) : ControllerBase
{
    // ---------------------------------------------------------------- metadados

    [HttpGet("meta/types")]
    public Task<IActionResult> Types(CancellationToken ct) => Run(() => devops.GetTypesAsync(ct));

    [HttpGet("meta/fields")]
    public Task<IActionResult> Fields([FromQuery] string? type, CancellationToken ct) => Run(() => devops.GetFieldsAsync(type ?? string.Empty, ct));

    [HttpGet("meta/tags")]
    public Task<IActionResult> Tags([FromQuery] string? q, CancellationToken ct) => Run(() => devops.GetTagsAsync(q, ct));

    [HttpGet("meta/areas")]
    public Task<IActionResult> Areas(CancellationToken ct) => Run(() => devops.GetAreasAsync(ct));

    [HttpGet("meta/iterations")]
    public Task<IActionResult> Iterations(CancellationToken ct) => Run(() => devops.GetIterationsAsync(ct));

    [HttpGet("meta/people")]
    public Task<IActionResult> People([FromQuery] string? q, CancellationToken ct) => Run(() => devops.GetPeopleAsync(q, ct));

    [HttpGet("teams")]
    public Task<IActionResult> Teams(CancellationToken ct) => Run(() => devops.GetTeamsAsync(ct));

    [HttpGet("boards")]
    public Task<IActionResult> Boards([FromQuery] string? team, CancellationToken ct) => Run(() => devops.GetBoardsAsync(team, ct));

    /// <summary>Configuração do board (cache de 60 s): colunas, lanes, mapeamento coluna→estado, campos, tipos, campo de ordem.</summary>
    [HttpGet("board")]
    public Task<IActionResult> Board([FromQuery] string? team, [FromQuery] string? board, CancellationToken ct) =>
        Run(() => devops.GetBoardAsync(team, board, null, ct));

    /// <summary>
    /// Cartões do board. <c>scope=epic</c>: só os cards do épico (<c>ids</c> da tela ou os do épico); senão os do time.
    /// </summary>
    [HttpPost("board/items")]
    public Task<IActionResult> BoardItems([FromBody] DevOpsBoardItemsRequest request, CancellationToken ct) =>
        Run(async () =>
        {
            IReadOnlyCollection<int>? ids = null;
            if (string.Equals(request.Scope, "epic", StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<string>? cards = request.Ids;
                if (cards is null)
                {
                    if (request.Epic is not > 0)
                        throw DevOpsDeskException.Invalid("Escopo epic: informe o épico (epic) ou os cards (ids).");
                    cards = await epicCards.GetCardsAsync(request.Epic.Value, ct)
                            ?? throw DevOpsDeskException.NotFoundDesk($"Épico #{request.Epic} não encontrado.");
                }
                ids = ParseIds(cards);
            }
            return await devops.GetBoardItemsAsync(request, ids, ct);
        });

    /// <summary>Consulta por WIQL ou filtros (viram WIQL), com os campos escolhidos (padrão: os principais).</summary>
    [HttpPost("query")]
    public Task<IActionResult> Query([FromBody] DevOpsQueryRequest request, CancellationToken ct) => Run(() => devops.QueryAsync(request, ct));

    // ---------------------------------------------------------------- cards

    [HttpGet("workitems/{id:int}")]
    public Task<IActionResult> Card(int id, CancellationToken ct) => Run(() => devops.GetCardAsync(id, ct));

    /// <summary>Cria o card (com épico: entra no épico como <c>created</c>).</summary>
    [HttpPost("workitems")]
    public Task<IActionResult> Create([FromBody] DevOpsCreateCardRequest request, CancellationToken ct) =>
        Run(async () => (await runner.CreateAsync(request, User, ct)).Card);

    /// <summary>Edita campos (null limpa), soma/tira tags e, com <c>comment</c>, comenta na discussion.</summary>
    [HttpPatch("workitems/{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] DevOpsUpdateCardRequest request, CancellationToken ct) =>
        Run(async () => (await runner.UpdateAsync(id, request, User, ct)).Card);

    /// <summary>Move de coluna (estado mapeado do tipo), done, lane e ordem (vizinhos).</summary>
    [HttpPost("workitems/{id:int}/move")]
    public Task<IActionResult> Move(int id, [FromBody] DevOpsMoveCardRequest request, CancellationToken ct) =>
        Run(async () => (await runner.MoveAsync(id, request, User, ct)).Card);

    /// <summary>Comentário na discussion (markdown → HTML).</summary>
    [HttpPost("workitems/{id:int}/comment")]
    public Task<IActionResult> Comment(int id, [FromBody] DevOpsCommentRequest request, CancellationToken ct) =>
        Run(async () => new { id, commentId = await runner.CommentAsync(id, request.Text, User, ct) });

    // ---------------------------------------------------------------- utilidades

    private static List<int> ParseIds(IEnumerable<string> cards) =>
        cards.Select(c => int.TryParse(c?.Trim().TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

    /// <summary>Resposta 200 ou o erro do DevOps em <c>{code, message}</c> (<c>error</c> igual, para o interceptor do front).</summary>
    private async Task<IActionResult> Run<T>(Func<Task<T>> body)
    {
        try
        {
            return Ok(await body());
        }
        catch (DevOpsActionException e)
        {
            var code = e is DevOpsDeskException desk ? desk.Code : e.StatusCode switch
            {
                404 => "not_found",
                409 => "conflict",
                400 => "invalid",
                _ => "devops_error"
            };
            return StatusCode(e.StatusCode, new { code, message = e.Message, error = e.Message });
        }
    }
}
