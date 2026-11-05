using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.azure.application.Contract;
using solvace.azure.domain.Options;
using solvace.prform.application.UserIntegrations;
using solvace.prform.domain.Entities;
using solvace.prform.domain.Extensions;
using solvace.prform.Knowledge;
using solvace.prform.Skills;

namespace solvace.prform.Controllers;

/// <summary>
/// Skills do Claude Code do PRMake (feature 0024): catálogo, pacotes, ferramenta de atualização e
/// instalador de um comando. Exige a api-key (nada público).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class SkillsController(SkillsCatalog catalog, IConfiguration configuration) : ControllerBase
{
    /// <summary>Skills publicadas (nome, descrição, versão) e a versão da ferramenta de atualização.</summary>
    [HttpGet]
    public ActionResult<object> List() => Ok(new { toolVersion = catalog.ToolVersion, skills = catalog.List() });

    /// <summary>Pacote .zip de uma skill (sem .venv).</summary>
    [HttpGet("{name}/package")]
    public ActionResult Package([FromRoute] string name)
    {
        var package = catalog.Package(name);
        if (package is null)
            return NotFound(new { error = $"Skill '{name}' não encontrada." });
        var version = catalog.List().First(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)).Version;
        return File(package, "application/zip", $"{name}-{version}.zip");
    }

    /// <summary>Ferramenta prmake-skills.sh (install/update/status).</summary>
    [HttpGet("tool")]
    public ContentResult Tool() => Content(catalog.Tool(ApiBase()), "text/x-shellscript; charset=utf-8");

    /// <summary>Instalador: curl -fsSL -H "x-api-key: …" …/Skills/install.sh | PRMAKE_TOKEN=… bash</summary>
    [HttpGet("install.sh")]
    public ContentResult Install() => Content(catalog.Installer(ApiBase()), "text/x-shellscript; charset=utf-8");

    /// <summary>
    /// Instalador do Windows (0035), no PowerShell:
    /// $env:PRMAKE_TOKEN='…'; irm -Headers @{'x-api-key'=$env:PRMAKE_TOKEN} …/Skills/install.ps1 | iex
    /// </summary>
    [HttpGet("install.ps1")]
    public ContentResult InstallPowerShell() => Content(catalog.InstallerPowerShell(ApiBase()), "text/plain; charset=utf-8");

    /// <summary>
    /// Configuração que as skills seguem (feature 0030), efetiva para o usuário: "Skills Configurations"
    /// (fluxo de branches, padrões), prompts do "AI Configurations" e nomes dos campos do DevOps. Valores
    /// JSON voltam como objeto; o resto como texto. Parte indisponível vem vazia (a skill avisa).
    /// </summary>
    [HttpGet("config")]
    public async Task<ActionResult<object>> Config([FromServices] SkillsConfigService configService, CancellationToken cancellationToken)
    {
        var c = await configService.BuildAsync(cancellationToken);
        return Ok(new
        {
            available = c.Available,
            settings = c.Settings,
            knowledge = c.Knowledge,
            prompts = new { bug = c.Prompts.Bug, userStory = c.Prompts.UserStory, summary = c.Prompts.Summary },
            fields = c.Fields
        });
    }

    /// <summary>
    /// 0085: "Regras do PRMake" calculadas para o card — fluxo pela área, branch principal, nome de branch por destino
    /// (BranchSuffixByTarget: release-version → hotfix/77434-rv), PRs do fluxo por tipo de repositório, padrões e regras
    /// fixas. <c>Accept: text/markdown</c> (ou <c>?format=md</c>) devolve só o markdown; senão JSON com o markdown e os
    /// dados calculados.
    /// </summary>
    [HttpGet("rules")]
    public async Task<ActionResult> Rules([FromQuery] string? card, [FromQuery] string? format, [FromServices] SkillsRulesService rules,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SkillsRulesService.NormalizeCard(card)))
            return BadRequest(new { error = "Informe o card (?card=)." });
        var r = await rules.GetAsync(card!, cancellationToken);
        var markdown = SkillsRulesService.Markdown(r);
        var wantsText = string.Equals(format, "md", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase)
                        || Request.Headers.Accept.Any(a => a?.Contains("text/markdown", StringComparison.OrdinalIgnoreCase) == true);
        if (wantsText)
            return Content(markdown, "text/markdown; charset=utf-8");
        return Ok(new
        {
            card = r.Card,
            markdown,
            workItemType = r.WorkItemType,
            isUserStory = r.IsUserStory,
            areaPath = r.AreaPath,
            flow = r.Flow,
            mainBranch = r.MainBranch,
            branchesByTarget = r.Suffixes.ToDictionary(s => s.Key, s => BranchRules.BranchFor(r.MainBranch, s.Key, r.Suffixes)),
            flowTargets = r.FlowTargets(),
            cardError = r.CardError
        });
    }

    /// <summary>URL pública da API para os scripts (Skills:ApiBase; senão o host da requisição, https fora do localhost).</summary>
    private string ApiBase()
    {
        var configured = configuration["Skills:ApiBase"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');
        var host = Request.Host.Host;
        var scheme = host is "localhost" or "127.0.0.1" ? Request.Scheme : "https";
        return $"{scheme}://{Request.Host}/api/v1";
    }
}
