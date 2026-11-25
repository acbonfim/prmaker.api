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
/// Ajustes 2: só <c>admin</c>/<c>management</c> (<see cref="DeskAccessAttribute"/>); token do caso, executor e skill
/// (<c>X-Execution-Client: skill</c>) continuam valendo.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
[DeskAccess]
public class DevOpsController(IDevOpsDeskService devops, DevOpsDeskRunner runner, IEpicCardSource epicCards) : ControllerBase
{
    // ---------------------------------------------------------------- metadados

    [HttpGet("meta/types")]
    [CardAccess]
    public Task<IActionResult> Types(CancellationToken ct) => Run(() => devops.GetTypesAsync(ct));

    [HttpGet("meta/fields")]
    [CardAccess]
    public Task<IActionResult> Fields([FromQuery] string? type, CancellationToken ct) => Run(() => devops.GetFieldsAsync(type ?? string.Empty, ct));

    [HttpGet("meta/tags")]
    [CardAccess]
    public Task<IActionResult> Tags([FromQuery] string? q, CancellationToken ct) => Run(() => devops.GetTagsAsync(q, ct));

    [HttpGet("meta/areas")]
    [CardAccess]
    public Task<IActionResult> Areas(CancellationToken ct) => Run(() => devops.GetAreasAsync(ct));

    [HttpGet("meta/iterations")]
    [CardAccess]
    public Task<IActionResult> Iterations(CancellationToken ct) => Run(() => devops.GetIterationsAsync(ct));

    /// <summary>
    /// Pessoas (ajustes 1): membros dos times + usuários da organização + busca de identidades (com <c>q</c> ≥ 2 letras) + quem
    /// aparece nos cards recentes, sem repetir. <c>q</c> sem caixa/acento; <c>top</c> padrão 50 com <c>q</c>, sem <c>q</c> todos.
    /// </summary>
    [HttpGet("meta/people")]
    [CardAccess]
    public Task<IActionResult> People([FromQuery] string? q, [FromQuery] int? top, CancellationToken ct) => Run(() => devops.GetPeopleAsync(q, top, ct));

    [HttpGet("teams")]
    public Task<IActionResult> Teams(CancellationToken ct) => Run(() => devops.GetTeamsAsync(ct));

    [HttpGet("boards")]
    public Task<IActionResult> Boards([FromQuery] string? team, CancellationToken ct) => Run(() => devops.GetBoardsAsync(team, ct));

    /// <summary>Configuração do board (cache de 60 s): colunas, lanes, mapeamento coluna→estado, campos, tipos, campo de ordem.</summary>
    [HttpGet("board")]
    [CardAccess]
    public Task<IActionResult> Board([FromQuery] string? team, [FromQuery] string? board, CancellationToken ct) =>
        Run(() => devops.GetBoardAsync(team, board, null, ct));

    /// <summary>
    /// Cartões do board. <c>scope=epic</c>: só os cards do épico (<c>ids</c> da tela ou os do épico); senão os do time. Ajustes 1:
    /// <c>useEpicFilters=true</c> com <c>epic</c> aplica no servidor os filtros salvos do épico (<c>filtersApplied</c> na resposta);
    /// sem time/board, os preferidos do épico.
    /// </summary>
    [HttpPost("board/items")]
    public Task<IActionResult> BoardItems([FromBody] DevOpsBoardItemsRequest request, CancellationToken ct) =>
        Run(() => LoadBoardItemsAsync(devops, epicCards, request, ct));

    /// <summary>
    /// Cartões do board com o épico resolvido (REST e MCP <c>prmake_devops_board</c>): cards do escopo <c>epic</c>, time/board
    /// preferidos quando faltam e, com <c>useEpicFilters</c>, os filtros salvos.
    /// </summary>
    public static async Task<DevOpsBoardItemsResponse> LoadBoardItemsAsync(IDevOpsDeskService devops, IEpicCardSource epicCards,
        DevOpsBoardItemsRequest request, CancellationToken ct)
    {
        EpicBoardInfo? epic = null;
        if (request.Epic is > 0 && (request.UseEpicFilters == true || string.IsNullOrWhiteSpace(request.Team) || string.IsNullOrWhiteSpace(request.Board)))
        {
            epic = await epicCards.GetBoardInfoAsync(request.Epic.Value, ct)
                   ?? throw DevOpsDeskException.NotFoundDesk($"Épico #{request.Epic} não encontrado.");
            if (string.IsNullOrWhiteSpace(request.Team) && !string.IsNullOrWhiteSpace(epic.Team))
                request.Team = epic.Team;
            // Board do épico só vale com o time do épico.
            if (string.IsNullOrWhiteSpace(request.Board) && !string.IsNullOrWhiteSpace(epic.Board)
                && string.Equals(request.Team?.Trim(), epic.Team?.Trim(), StringComparison.OrdinalIgnoreCase))
                request.Board = epic.Board;
        }

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
        var filters = request.UseEpicFilters == true ? epic?.Filters : null;
        return await devops.GetBoardItemsAsync(request, ids, filters, ct);
    }

    /// <summary>Consulta por WIQL ou filtros (viram WIQL), com os campos escolhidos (padrão: os principais).</summary>
    [HttpPost("query")]
    [CardAccess]
    public Task<IActionResult> Query([FromBody] DevOpsQueryRequest request, CancellationToken ct) => Run(() => devops.QueryAsync(request, ct));

    // ---------------------------------------------------------------- cards

    [HttpGet("workitems/{id:int}")]
    [CardAccess]
    public Task<IActionResult> Card(int id, CancellationToken ct) => Run(() => devops.GetCardAsync(id, ct));

    /// <summary>Cria o card (com épico: entra no épico como <c>created</c>).</summary>
    [HttpPost("workitems")]
    public Task<IActionResult> Create([FromBody] DevOpsCreateCardRequest request, CancellationToken ct) =>
        Run(async () => (await runner.CreateAsync(request, User, ct)).Card);

    /// <summary>Edita campos (null limpa), soma/tira tags e, com <c>comment</c>, comenta na discussion.</summary>
    [HttpPatch("workitems/{id:int}")]
    [CardAccess]
    public Task<IActionResult> Update(int id, [FromBody] DevOpsUpdateCardRequest request, CancellationToken ct) =>
        Run(async () => (await runner.UpdateAsync(id, request, User, ct)).Card);

    /// <summary>Move de coluna (estado mapeado do tipo), done, lane e ordem (vizinhos).</summary>
    [HttpPost("workitems/{id:int}/move")]
    [CardAccess]
    public Task<IActionResult> Move(int id, [FromBody] DevOpsMoveCardRequest request, CancellationToken ct) =>
        Run(async () => (await runner.MoveAsync(id, request, User, ct)).Card);

    /// <summary>Comentário na discussion (markdown → HTML; <c>#123</c> e <c>@Nome</c> das <c>mentions</c> viram menções do DevOps).</summary>
    [HttpPost("workitems/{id:int}/comment")]
    [CardAccess]
    public Task<IActionResult> Comment(int id, [FromBody] DevOpsCommentRequest request, CancellationToken ct) =>
        Run(async () => new { id, commentId = await runner.CommentAsync(id, request.Text, User, ct, mentions: request.Mentions) });

    /// <summary>Ajustes 3: discussion do card, mais novos primeiro (até 200).</summary>
    [HttpGet("workitems/{id:int}/comments")]
    [CardAccess]
    public Task<IActionResult> Comments(int id, [FromQuery] int? top, CancellationToken ct) =>
        Run(() => devops.GetCommentsAsync(id, top ?? 200, ct));

    /// <summary>Ajustes 3: anexos do card e imagens/arquivos colados nos campos e na discussion.</summary>
    [HttpGet("workitems/{id:int}/attachments")]
    [CardAccess]
    public Task<IActionResult> Attachments(int id, CancellationToken ct) => Run(() => devops.GetAttachmentsAsync(id, ct));

    /// <summary>Modal único: histórico do card (revisões com campos antes → depois, links e comentários), mais novas primeiro.</summary>
    [HttpGet("workitems/{id:int}/history")]
    [CardAccess]
    public Task<IActionResult> History(int id, CancellationToken ct) => Run(() => devops.GetHistoryAsync(id, ct));

    /// <summary>Modal único: time/board em que o card aparece (a Bancada não tem épico para dizer) — <c>{team, board}</c>, nulos sem board.</summary>
    [HttpGet("workitems/{id:int}/board")]
    [CardAccess]
    public Task<IActionResult> CardBoard(int id, CancellationToken ct) => Run(() => devops.GetCardBoardAsync(id, ct));

    /// <summary>Limite do anexo servido pela Mesa (o resto, abrir no DevOps).</summary>
    public const long MaxAttachmentBytes = 25L * 1024 * 1024;

    /// <summary>Ajustes 3: conteúdo de um anexo pelo GUID (inline; <c>download=true</c> baixa com o nome). Passou de 25 MB → 413.</summary>
    [HttpGet("workitems/{id:int}/attachments/{attachmentId:guid}")]
    [CardAccess]
    public async Task<IActionResult> AttachmentContent(int id, Guid attachmentId, [FromQuery] string? name, [FromQuery] bool download = false,
        CancellationToken ct = default)
    {
        try
        {
            var file = await devops.DownloadAttachmentAsync(attachmentId, name, MaxAttachmentBytes, ct);
            if (file.TooLarge)
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new
                {
                    code = "too_large",
                    message = $"O anexo {file.Name} passa de 25 MB — abra no DevOps.",
                    error = $"O anexo {file.Name} passa de 25 MB — abra no DevOps."
                });
            Response.Headers.CacheControl = "private, max-age=600";
            if (download) return File(file.Data, file.ContentType, file.Name);
            var disposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("inline");
            disposition.SetHttpFileName(file.Name);
            Response.Headers.ContentDisposition = disposition.ToString();
            return File(file.Data, file.ContentType);
        }
        catch (DevOpsActionException e)
        {
            var code = e is DevOpsDeskException desk ? desk.Code : "devops_error";
            return StatusCode(e.StatusCode, new { code, message = e.Message, error = e.Message });
        }
    }

    // ---------------------------------------------------------------- utilidades

    public static List<int> ParseIds(IEnumerable<string> cards) =>
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
