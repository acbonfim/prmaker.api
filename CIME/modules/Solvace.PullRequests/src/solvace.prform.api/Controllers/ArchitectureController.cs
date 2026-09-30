using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.knowledge.application.Contracts;
using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Requests;
using solvace.knowledge.domain.Responses;

namespace solvace.prform.Controllers;

/// <summary>
/// Engenharia reversa da Solvace (feature 0033): leitura para qualquer usuário logado; escrita (skill
/// mapear/atualizar, edição na tela) só admin. O índice e o pacote do espelho local servem as skills.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ArchitectureController(IArchitectureApplication application, solvace.timeline.application.Contracts.IUserRepository users) : ControllerBase
{
    public const string HashHeader = "X-Kb-Hash";

    [HttpGet("projects")]
    public Task<ActionResult<List<ArchitectureProjectResponse>>> Projects(CancellationToken ct) =>
        Run<List<ArchitectureProjectResponse>>(async () => Ok(await application.ListProjectsAsync(ct)));

    [HttpGet("projects/{key}")]
    public Task<ActionResult<ArchitectureProjectResponse>> Project([FromRoute] string key, CancellationToken ct) =>
        Run<ArchitectureProjectResponse>(async () => Ok(await application.GetProjectAsync(key, ct)));

    [Authorize(Roles = "admin")]
    [HttpPut("projects/{key}")]
    public Task<ActionResult<ArchitectureProjectResponse>> UpsertProject([FromRoute] string key, [FromBody] UpsertArchitectureProjectRequest request, CancellationToken ct) =>
        Run<ArchitectureProjectResponse>(async () => Ok(await application.UpsertProjectAsync(key, request, await ActorAsync(ct), ct)));

    [Authorize(Roles = "admin")]
    [HttpDelete("projects/{key}")]
    public Task<ActionResult<object>> DeleteProject([FromRoute] string key, CancellationToken ct) =>
        Run<object>(async () =>
        {
            await application.DeleteProjectAsync(key, await ActorAsync(ct), ct);
            return NoContent();
        });

    [HttpGet("projects/{key}/sections/{section}")]
    public Task<ActionResult<ArchitectureSectionResponse>> Section([FromRoute] string key, [FromRoute] string section, CancellationToken ct) =>
        Run<ArchitectureSectionResponse>(async () => Ok(await application.GetSectionAsync(key, section, ct)));

    [Authorize(Roles = "admin")]
    [HttpPut("projects/{key}/sections/{section}")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public Task<ActionResult<ArchitectureSectionResponse>> WriteSection([FromRoute] string key, [FromRoute] string section,
        [FromBody] WriteArchitectureSectionRequest request, CancellationToken ct) =>
        Run<ArchitectureSectionResponse>(async () => Ok(await application.WriteSectionAsync(key, section, request, await ActorAsync(ct), ct)));

    [HttpGet("projects/{key}/sections/{section}/versions")]
    public Task<ActionResult<List<ArchitectureSectionVersionResponse>>> Versions([FromRoute] string key, [FromRoute] string section, CancellationToken ct) =>
        Run<List<ArchitectureSectionVersionResponse>>(async () => Ok(await application.GetVersionsAsync(key, section, ct)));

    [HttpGet("projects/{key}/sections/{section}/versions/{version:int}")]
    public Task<ActionResult<ArchitectureSectionVersionResponse>> Version([FromRoute] string key, [FromRoute] string section, [FromRoute] int version, CancellationToken ct) =>
        Run<ArchitectureSectionVersionResponse>(async () => Ok(await application.GetVersionAsync(key, section, version, ct)));

    /// <summary>Índice compacto em markdown (o que a skill lê primeiro).</summary>
    [HttpGet("index")]
    public async Task<ContentResult> Index(CancellationToken ct) =>
        Content(await application.BuildIndexAsync(ct), "text/markdown; charset=utf-8");

    /// <summary>Hash do pacote — a skill só baixa o .zip quando muda.</summary>
    [HttpGet("export/manifest")]
    public async Task<ActionResult<ArchitectureExportManifest>> Manifest(CancellationToken ct) => Ok(await application.GetManifestAsync(ct));

    /// <summary>Pacote do espelho local (~/.claude/solvace-kb): índice, seções e artigos do KC.</summary>
    [HttpGet("export")]
    public async Task<ActionResult> Export(CancellationToken ct)
    {
        var (manifest, zip) = await application.ExportAsync(ct);
        Response.Headers[HashHeader] = manifest.Hash;
        return File(zip, "application/zip", $"solvace-kb-{manifest.Hash}.zip");
    }

    private Task<string> ActorAsync(CancellationToken ct) => KnowledgeController.ActorAsync(users, User, ct);

    private async Task<ActionResult<T>> Run<T>(Func<Task<ActionResult>> action)
    {
        try { return await action(); }
        catch (DomainException e) { return BadRequest(new { error = e.Message }); }
        catch (KnowledgeNotFoundException e) { return NotFound(new { error = e.Message }); }
    }
}
