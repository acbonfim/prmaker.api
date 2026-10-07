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
public class ArchitectureController(IArchitectureApplication application, solvace.timeline.application.Contracts.IUserRepository users,
    solvace.prform.Knowledge.ArchitectureChatService chat, solvace.prform.Knowledge.ArchitectureAskService ask,
    solvace.prform.Knowledge.ArchitectureGuideService guide, solvace.prform.Knowledge.ArchitectureLearnService learn,
    ILogger<ArchitectureController> logger) : ControllerBase
{
    public const string HashHeader = "X-Kb-Hash";

    [HttpGet("projects")]
    public Task<ActionResult<List<ArchitectureProjectResponse>>> Projects(CancellationToken ct) =>
        Run<List<ArchitectureProjectResponse>>(async () => Ok(await application.ListProjectsAsync(ct)));

    /// <summary>Grafo do ecossistema: projetos, serviços externos e interdependências (0034).</summary>
    [HttpGet("graph")]
    public async Task<ActionResult<ArchitectureGraphResponse>> Graph(CancellationToken ct) => Ok(await application.GetGraphAsync(ct));

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
    [RequestSizeLimit(16 * 1024 * 1024)] // seção re-* até 5 milhões de caracteres (acento = 2 bytes)
    public Task<ActionResult<ArchitectureSectionResponse>> WriteSection([FromRoute] string key, [FromRoute] string section,
        [FromBody] WriteArchitectureSectionRequest request, CancellationToken ct) =>
        Run<ArchitectureSectionResponse>(async () => Ok(await application.WriteSectionAsync(key, section, request, await ActorAsync(ct), ct)));

    [HttpGet("projects/{key}/sections/{section}/versions")]
    public Task<ActionResult<List<ArchitectureSectionVersionResponse>>> Versions([FromRoute] string key, [FromRoute] string section, CancellationToken ct) =>
        Run<List<ArchitectureSectionVersionResponse>>(async () => Ok(await application.GetVersionsAsync(key, section, ct)));

    [HttpGet("projects/{key}/sections/{section}/versions/{number:int}")]
    public Task<ActionResult<ArchitectureSectionVersionResponse>> Version([FromRoute] string key, [FromRoute] string section, [FromRoute] int number, CancellationToken ct) =>
        Run<ArchitectureSectionVersionResponse>(async () => Ok(await application.GetVersionAsync(key, section, number, ct)));

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

    /// <summary>Sugestão para a base (a skill propõe o que aprendeu/divergências; qualquer usuário logado). Não grava na seção.</summary>
    [HttpPost("suggestions")]
    public Task<ActionResult<ArchitectureSuggestionResponse>> Suggest([FromBody] CreateArchitectureSuggestionRequest request, CancellationToken ct) =>
        Run<ArchitectureSuggestionResponse>(async () => Ok(await application.SuggestAsync(request, await ActorAsync(ct), ct)));

    /// <summary>Fila de sugestões (padrão: pendentes).</summary>
    [Authorize(Roles = "admin")]
    [HttpGet("suggestions")]
    public Task<ActionResult<List<ArchitectureSuggestionResponse>>> Suggestions([FromQuery] string? status = "pending", CancellationToken ct = default) =>
        Run<List<ArchitectureSuggestionResponse>>(async () => Ok(await application.GetSuggestionsAsync(status == "all" ? null : status, ct)));

    [Authorize(Roles = "admin")]
    [HttpPost("suggestions/{id:guid}/resolve")]
    public Task<ActionResult<ArchitectureSuggestionResponse>> ResolveSuggestion([FromRoute] Guid id, [FromBody] ResolveArchitectureSuggestionRequest request, CancellationToken ct) =>
        Run<ArchitectureSuggestionResponse>(async () => Ok(await application.ResolveSuggestionAsync(id, request, await ActorAsync(ct), ct)));

    /// <summary>O chat de melhoria está disponível? (admin e plugin de IA configurado)</summary>
    [Authorize(Roles = "admin")]
    [HttpGet("chat/status")]
    public async Task<ActionResult<solvace.prform.Knowledge.ArchitectureChatStatus>> ChatStatus(CancellationToken ct) => Ok(await chat.GetStatusAsync(ct));

    /// <summary>Conversa com o especialista sobre uma seção; pode devolver a seção inteira proposta (nada é gravado).</summary>
    [Authorize(Roles = "admin")]
    [HttpPost("projects/{key}/sections/{section}/chat")]
    public Task<ActionResult<solvace.prform.Knowledge.ArchitectureChatResponse>> Chat([FromRoute] string key, [FromRoute] string section,
        [FromBody] solvace.prform.Knowledge.ArchitectureChatRequest request, CancellationToken ct) =>
        Run<solvace.prform.Knowledge.ArchitectureChatResponse>(async () =>
        {
            try { return Ok(await chat.ChatAsync(key, section, request, ct)); }
            catch (InvalidOperationException e) { return StatusCode(StatusCodes.Status502BadGateway, new { error = e.Message }); }
        });

    /// <summary>Busca no conteúdo das seções e dos artigos do KC (0037) — qualquer usuário logado.</summary>
    [HttpGet("search")]
    public Task<ActionResult<List<ArchitectureSearchHit>>> Search([FromQuery] string? q, [FromQuery] int limit, CancellationToken ct) =>
        Run<List<ArchitectureSearchHit>>(async () => Ok(await application.SearchAsync(q ?? string.Empty, limit <= 0 ? 20 : limit, null, null, null, ct)));

    /// <summary>A pergunta à base com IA está disponível? (plugin AI Configurations) — 0037.</summary>
    [HttpGet("ask/status")]
    public async Task<ActionResult<solvace.prform.Knowledge.ArchitectureChatStatus>> AskStatus(CancellationToken ct) => Ok(await chat.GetStatusAsync(ct));

    /// <summary>"Pergunte à Base Solvace" (0037): a IA entende a pergunta e leva aos trechos que respondem.</summary>
    [HttpPost("ask")]
    public Task<ActionResult<ArchitectureAskResponse>> Ask([FromBody] solvace.prform.Knowledge.ArchitectureAskRequest request,
        [FromServices] IReverseEngineeringApplication reverse, CancellationToken ct) =>
        Run<ArchitectureAskResponse>(async () =>
        {
            var response = await ask.AskAsync(request.Question, ct);
            if (response.AiUsed)
            {
                await RecordAsync(response.Question, response.Kind, response.Coverage, response.SuggestedSection?.ProjectKey, response.SuggestedSection?.SectionKey, ct);
                // 0054: sem resposta num módulo com engenharia reversa → vira pergunta prática a responder (visão prática)
                if (response.Coverage is "not-found" or "partial" && response.SuggestedSection?.ProjectKey is { } project)
                    try { await reverse.RecordQuestionGapAsync(project, response.Question, await ActorAsync(ct), ct); }
                    catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning(e, "Lacuna do Pergunte não virou sugestão"); }
            }
            return Ok(response);
        });

    /// <summary>"Analisar a fundo" (0038): lê as seções inteiras dos projetos prováveis e propõe a seção que documenta o assunto (não grava).</summary>
    [HttpPost("ask/deep")]
    public Task<ActionResult<ArchitectureDeepAnswerResponse>> AskDeep([FromBody] solvace.prform.Knowledge.ArchitectureAskRequest request, CancellationToken ct) =>
        Run<ArchitectureDeepAnswerResponse>(async () =>
        {
            var response = await ask.DeepAsync(request.Question, ct);
            if (response.AiUnavailableReason is null)
                await RecordAsync(response.Question, null, response.Coverage, response.Proposal?.ProjectKey, response.Proposal?.SectionKey, ct);
            return Ok(response);
        });

    /// <summary>Perguntas feitas no "Pergunte" (0040): padrão = as sem resposta (not-found/partial) em aberto, mais perguntadas primeiro.</summary>
    [Authorize(Roles = "admin")]
    [HttpGet("questions")]
    public Task<ActionResult<List<ArchitectureQuestionResponse>>> Questions([FromQuery] string? status = "open", [FromQuery] bool gaps = true, CancellationToken ct = default) =>
        Run<List<ArchitectureQuestionResponse>>(async () => Ok(await application.GetQuestionsAsync(status == "all" ? null : status, gaps, ct)));

    /// <summary>Marca a pergunta como respondida (com a seção que responde), descartada ou reaberta (0040).</summary>
    [Authorize(Roles = "admin")]
    [HttpPost("questions/{id:guid}/resolve")]
    public Task<ActionResult<ArchitectureQuestionResponse>> ResolveQuestion([FromRoute] Guid id, [FromBody] ResolveArchitectureQuestionRequest request, CancellationToken ct) =>
        Run<ArchitectureQuestionResponse>(async () => Ok(await application.ResolveQuestionAsync(id, request, await ActorAsync(ct), ct)));

    /// <summary>Registra a pergunta sem atrapalhar a resposta (falha no registro só vai para o log).</summary>
    private async Task RecordAsync(string question, string? kind, string? coverage, string? project, string? section, CancellationToken ct)
    {
        try { await application.RecordQuestionAsync(question, kind, coverage, project, section, await ActorAsync(ct), ct); }
        catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning(e, "Não registrei a pergunta da Base Solvace"); }
    }

    /// <summary>"Gerar guia com a IA" (0038): propõe o Guia do projeto em linguagem simples + nome/frase/área (não grava).</summary>
    [Authorize(Roles = "admin")]
    [HttpPost("projects/{key}/guide")]
    public Task<ActionResult<ArchitectureGuideResponse>> Guide([FromRoute] string key, [FromBody] solvace.prform.Knowledge.ArchitectureGuideRequest? request, CancellationToken ct) =>
        Run<ArchitectureGuideResponse>(async () =>
        {
            try { return Ok(await guide.GenerateAsync(key, request ?? new(), ct)); }
            catch (solvace.prform.Knowledge.ArchitectureAiUnavailableException e) { return Conflict(new { error = e.Message }); }
            catch (InvalidOperationException e) { return StatusCode(StatusCodes.Status502BadGateway, new { error = e.Message }); }
        });

    /// <summary>"Aprender com um card" (0038): junta o que foi feito no card e propõe aprendizados (não grava — a tela envia para a fila).</summary>
    [HttpPost("learn-from-card")]
    public Task<ActionResult<LearnFromCardResponse>> LearnFromCard([FromBody] solvace.prform.Knowledge.LearnFromCardRequest request, CancellationToken ct) =>
        Run<LearnFromCardResponse>(async () => Ok(await learn.LearnAsync(request, ct)));

    private Task<string> ActorAsync(CancellationToken ct) => KnowledgeController.ActorAsync(users, User, ct);

    private async Task<ActionResult<T>> Run<T>(Func<Task<ActionResult>> action)
    {
        try { return await action(); }
        catch (DomainException e) { return BadRequest(new { error = e.Message }); }
        catch (KnowledgeNotFoundException e) { return NotFound(new { error = e.Message }); }
    }
}
