using Microsoft.AspNetCore.Mvc;
using solvace.github.application.Contract;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;
using solvace.timeline.application.Contracts;
using solvace.timeline.domain.Requests;

namespace solvace.prform.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class PullRequestController : ControllerBase
{
    private readonly IPullRequestApplication _application;
    private readonly ILogger<PullRequestController> _logger;

    // IPullRequestGithubApplication/ITimelineApplication são injetados por ação ([FromServices]):
    // o GitHubService lança exceção no construtor se o plugin do GitHub não estiver configurado,
    // e isso não pode derrubar as rotas do card (buscar/salvar) que não usam o GitHub.
    public PullRequestController(IPullRequestApplication application, ILogger<PullRequestController> logger)
    {
        _application = application;
        _logger = logger;
    }

    /// <summary>
    /// Salva o registro do card (um por card). Description/RootCause opcionais;
    /// branchPrefix/branchName/repositoryId são aceitos e ignorados (compatibilidade).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PullRequestRegisterResponse>> Create(PullRequestRegisterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _application.Create(request, cancellationToken);
            return Ok(created);
        }
        catch (DomainException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }

    /// <param name="repositoryId">Legado: aceito e ignorado.</param>
    [HttpGet]
    public async Task<ActionResult<PullRequestRegisterResponse>> Get(int id, CancellationToken cancellationToken, string? repositoryId = null)
    {
        var user = await _application.Get(id, cancellationToken);

        return Ok(user);
    }

    /// <param name="repositoryId">Legado: aceito e ignorado (há um registro por card).</param>
    [HttpGet("GetByCardNumber")]
    public async Task<ActionResult<PullRequestRegisterResponse>> GetByCardNumber(string cardNumber, CancellationToken cancellationToken, string? repositoryId = null)
    {
        var response = await _application.GetByCardNumber(cardNumber, cancellationToken);

        return Ok(response);
    }

    [HttpGet("GetRecentByUser")]
    public async Task<ActionResult<IReadOnlyList<PullRequestRecentResponse>>> GetRecentByUser(Guid userId, CancellationToken cancellationToken, int take = 5)
    {
        var response = await _application.GetRecentByUser(userId, take, cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Abre o PR no GitHub e registra no card. Se já existir PR aberto para head→base,
    /// devolve o existente com alreadyExisted = true.
    /// </summary>
    [HttpPost("{cardNumber}/github")]
    public async Task<ActionResult<PullRequestGithubResponse>> OpenGithubPullRequest(string cardNumber, OpenPullRequestGithubRequest request,
        [FromServices] IPullRequestGithubApplication githubApplication, [FromServices] ITimelineApplication timelineApplication,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await githubApplication.Open(cardNumber, request, cancellationToken);
            await RegisterOnTimelineAsync(timelineApplication, created, request.UserId, cancellationToken);
            return Ok(created);
        }
        catch (DomainException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }

    /// <summary>Atualiza título/descrição de um PR já aberto (GitHub + registro).</summary>
    [HttpPut("{cardNumber}/github/{id:int}")]
    public async Task<ActionResult<PullRequestGithubResponse>> UpdateGithubPullRequest(string cardNumber, int id, UpdatePullRequestGithubRequest request,
        [FromServices] IPullRequestGithubApplication githubApplication, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await githubApplication.Update(cardNumber, id, request, cancellationToken));
        }
        catch (DomainException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }

    /// <summary>
    /// PRs do GitHub do card, mais recentes primeiro. refreshStatus consulta no GitHub
    /// apenas os PRs ainda abertos (MERGED/CLOSED usam o status persistido); forceRefresh
    /// ignora o cache de status (60 s).
    /// </summary>
    [HttpGet("{cardNumber}/github")]
    public async Task<ActionResult<IReadOnlyList<PullRequestGithubResponse>>> ListGithubPullRequests(string cardNumber,
        [FromServices] IPullRequestGithubApplication githubApplication, CancellationToken cancellationToken,
        bool refreshStatus = true, bool forceRefresh = false)
    {
        try
        {
            return Ok(await githubApplication.ListByCard(cardNumber, refreshStatus, cancellationToken, forceRefresh));
        }
        catch (DomainException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }

    /// <summary>
    /// Registra na linha do tempo do card que o PR foi aberto (ou que um PR já existente foi
    /// registrado). Falha aqui não desfaz nem falha a abertura do PR — só é logada.
    /// </summary>
    private async Task RegisterOnTimelineAsync(ITimelineApplication timelineApplication, PullRequestGithubResponse pr, Guid requestUserId, CancellationToken cancellationToken)
    {
        var description = pr.AlreadyExisted
            ? $"PR #{pr.Number} já existente registrado pelo CIME: {pr.RepositoryId} ({pr.BranchPrefix}{pr.BranchName} → {pr.TargetBranch}) — {pr.Url}"
            : $"PR #{pr.Number} aberto pelo CIME: {pr.RepositoryId} ({pr.BranchPrefix}{pr.BranchName} → {pr.TargetBranch}) — {pr.Url}";

        // Mesmo critério do TimelineController: usuário logado (claim ExternalId); senão, o do request.
        var claim = User.FindFirst("ExternalId")?.Value;
        Guid? userId = !string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out var id) ? id
            : requestUserId != Guid.Empty ? requestUserId : null;

        try
        {
            await timelineApplication.CreateAsync(new CreateTimelineEntryRequest
            {
                CardNumber = pr.CardNumber,
                Description = description,
                UserName = userId is null ? "CIME" : null
            }, userId, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "PR #{Number} aberto, mas não foi possível registrar na timeline do card {CardNumber}", pr.Number, pr.CardNumber);
        }
    }
}
