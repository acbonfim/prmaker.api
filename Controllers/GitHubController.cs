using Microsoft.AspNetCore.Mvc;
using Octokit;

namespace solvace.prform.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GitHubController : ControllerBase
{
    private readonly GitHubClient _gitHubClient;
    private readonly IConfiguration _configuration;

    public GitHubController(IConfiguration configuration)
    {
        _configuration = configuration;

        var token = _configuration["GITHUB_TOKEN"];
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("GITHUB_TOKEN não configurado");

        _gitHubClient = new GitHubClient(new ProductHeaderValue("SolvacePRForm"))
        {
            Credentials = new Credentials(token)
        };
    }

    /// <summary>
    /// Testa a configuração do GitHub
    /// </summary>
    /// <returns>Status da configuração</returns>
    [HttpGet("test")]
    public IActionResult TestConfiguration()
    {
        try
        {
            var owner = _configuration["GITHUB_OWNER"];
            var repo = _configuration["GITHUB_REPO"];
            var branch = _configuration["GITHUB_BRANCH"];
            var token = _configuration["GITHUB_TOKEN"];

            return Ok(new
            {
                owner = owner,
                repo = repo,
                branch = branch,
                hasToken = !string.IsNullOrEmpty(token),
                tokenLength = token?.Length ?? 0,
                message = "Configuração do GitHub carregada com sucesso"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Consulta template do GitHub baseado na branch
    /// </summary>
    /// <param name="branch">Nome da branch para buscar o template</param>
    /// <returns>Conteúdo do template</returns>
    [HttpGet("template")]
    public async Task<IActionResult> GetTemplate([FromQuery] string branch)
    {
        try
        {
            var owner = _configuration["GITHUB_OWNER"];
            var repo = _configuration["GITHUB_REPO"];
            var defaultBranch = _configuration["GITHUB_BRANCH"];
            var workflowPath = ".github/workflows/apply_pr_template.yml";

            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo) || string.IsNullOrEmpty(defaultBranch))
            {
                return BadRequest(new
                {
                    error = "Configurações do GitHub não encontradas",
                    owner = owner ?? "não configurado",
                    repo = repo ?? "não configurado",
                    branch = defaultBranch ?? "não configurado"
                });
            }

            if (string.IsNullOrEmpty(branch))
            {
                return BadRequest(new { error = "Parâmetro 'branch' é obrigatório" });
            }

            // Buscar o arquivo de workflow
            var workflowContent = await _gitHubClient.Repository.Content.GetRawContentByRef(
                owner, repo, workflowPath, defaultBranch);

            var yamlContent = System.Text.Encoding.UTF8.GetString(workflowContent);

            // Regex para encontrar o template baseado na branch
            var regex = new System.Text.RegularExpressions.Regex($"\"{branch}\"\\)[\\s\\S]*?TEMPLATE=\"(.+?)\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var match = regex.Match(yamlContent);

            // Fallback para hotfix
            if (!match.Success && branch.Contains("hotfix"))
            {
                var fallbackRegex = new System.Text.RegularExpressions.Regex($"\"release-version\"\\)[\\s\\S]*?TEMPLATE=\"(.+?)\"",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                match = fallbackRegex.Match(yamlContent);
            }

            if (!match.Success)
            {
                return NotFound($"Template para a branch \"{branch}\" não encontrado.");
            }

            var templatePath = match.Groups[1].Value;

            // Buscar o conteúdo do template
            var templateContent = await _gitHubClient.Repository.Content.GetRawContentByRef(
                owner, repo, templatePath, defaultBranch);

            var templateText = System.Text.Encoding.UTF8.GetString(templateContent);

            return Content(templateText, "text/plain");
        }
        catch (Octokit.NotFoundException ex)
        {
            Console.WriteLine($"Arquivo não encontrado no GitHub: {ex.Message}");
            return NotFound(new { error = "Arquivo ou template não encontrado no GitHub", details = ex.Message });
        }
        catch (Octokit.ApiException ex)
        {
            Console.WriteLine($"Erro na API do GitHub: {ex.Message}");
            return StatusCode((int)ex.StatusCode, new { error = "Erro ao acessar GitHub API", details = ex.Message, statusCode = ex.StatusCode });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao acessar GitHub: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return StatusCode(500, new { error = "Erro ao buscar template do GitHub", details = ex.Message });
        }
    }
}
