using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
