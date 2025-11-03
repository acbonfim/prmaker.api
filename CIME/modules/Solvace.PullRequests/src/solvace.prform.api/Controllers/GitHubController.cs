using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using solvace.github.application.Contract;

namespace solvace.prform.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class GitHubController : ControllerBase
{
    private readonly IGitHubService _githubService;

    public GitHubController(IGitHubService githubService)
    {
        _githubService = githubService;
    }

    [HttpPost("pull-request")]
    [Consumes("text/plain", "text/markdown", "text/html", "application/json")]
    public async Task<IActionResult> CreatePullRequest(
    [FromQuery] string sourceBranch,
    [FromQuery] string targetBranch,
    [FromQuery] string title,
    [FromQuery] bool draft,
    CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var result = await _githubService.CreatePullRequestAsync(sourceBranch, targetBranch, title, draft, body, cancellationToken);
        if (result == null)
            return BadRequest(new { error = "Erro ao criar o PR" });
        return Ok(result);
    }

    [HttpGet("card-references/{cardNumber}")]
    public async Task<IActionResult> GetCardReferences(string cardNumber, int maxPerType, CancellationToken cancellationToken)
    {
        var result = await _githubService.GetCardReferencesAsync(cardNumber, maxPerType, cancellationToken);
        if (result.Error != null)
            return BadRequest(new { error = result.Error });
        return Ok(result);
    }

    [HttpGet("commit/{sha}/diff")]
    public async Task<IActionResult> GetCommitDiff(string sha, CancellationToken cancellationToken)
    {
        var result = await _githubService.GetCommitDiffAsync(sha, cancellationToken);
        if (result.Error != null)
            return BadRequest(new { error = result.Error });
        return Ok(result);
    }

    [HttpGet("compare")]
    public async Task<IActionResult> CompareRefs(string @base, string head, CancellationToken cancellationToken)
    {
        var result = await _githubService.CompareRefsDiffAsync(@base, head, cancellationToken);
        if (result.Error != null)
            return BadRequest(new { error = result.Error });
        return Ok(result);
    }
}
