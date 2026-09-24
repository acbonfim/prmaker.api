using Microsoft.AspNetCore.Mvc;
using solvace.github.application.Contract;
using solvace.prform.application.Contracts;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;

namespace solvace.prform.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class PullRequestController : ControllerBase
{
    private readonly IPullRequestApplication _application;
    private readonly IPullRequestGithubApplication _githubApplication;

    public PullRequestController(IPullRequestApplication application, IPullRequestGithubApplication githubApplication)
    {
        _application = application;
        _githubApplication = githubApplication;
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
    public async Task<ActionResult<PullRequestGithubResponse>> OpenGithubPullRequest(string cardNumber, OpenPullRequestGithubRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _githubApplication.Open(cardNumber, request, cancellationToken));
        }
        catch (DomainException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }

    /// <summary>Atualiza título/descrição de um PR já aberto (GitHub + registro).</summary>
    [HttpPut("{cardNumber}/github/{id:int}")]
    public async Task<ActionResult<PullRequestGithubResponse>> UpdateGithubPullRequest(string cardNumber, int id, UpdatePullRequestGithubRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _githubApplication.Update(cardNumber, id, request, cancellationToken));
        }
        catch (DomainException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }

    /// <summary>
    /// PRs do GitHub do card, mais recentes primeiro. refreshStatus consulta no GitHub
    /// apenas os PRs ainda abertos (MERGED/CLOSED usam o status persistido).
    /// </summary>
    [HttpGet("{cardNumber}/github")]
    public async Task<ActionResult<IReadOnlyList<PullRequestGithubResponse>>> ListGithubPullRequests(string cardNumber, CancellationToken cancellationToken, bool refreshStatus = true)
    {
        try
        {
            return Ok(await _githubApplication.ListByCard(cardNumber, refreshStatus, cancellationToken));
        }
        catch (DomainException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }
}
