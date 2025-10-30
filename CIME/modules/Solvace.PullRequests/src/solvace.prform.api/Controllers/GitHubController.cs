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
    private readonly IConfiguration _configuration;
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
        return new ContentResult { StatusCode = result.StatusCode, Content = result.Content, ContentType = result.ContentType };
    }

    [HttpGet("card-references/{cardNumber}")]
    public async Task<IActionResult> GetCardReferences(string cardNumber, int maxPerType, CancellationToken cancellationToken)
    {
        var result = await _githubService.GetCardReferencesAsync(cardNumber, maxPerType, cancellationToken);
        return new ContentResult { StatusCode = result.StatusCode, Content = result.Content, ContentType = result.ContentType };
    }
}
