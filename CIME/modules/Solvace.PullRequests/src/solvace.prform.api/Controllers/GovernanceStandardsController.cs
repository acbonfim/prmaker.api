using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.azure.application.Contract;
using solvace.azure.domain.Exceptions;
using solvace.governance.application;
using solvace.governance.domain;
using solvace.governance.domain.Entities;
using solvace.governance.domain.Standards;
using solvace.knowledge.application.Contracts;
using solvace.prform.Governance;
using solvace.prform.Skills;

namespace solvace.prform.Controllers;

/// <summary>
/// 0101: padrões de banco e de aplicação. Leitura, padrões do card e conferência valem para todo usuário autenticado
/// (as skills e o hook usam); editar e importar (wiki pela integração DevOps de quem pede, engenharia reversa) é do admin.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Governance/standards")]
[Authorize]
public class GovernanceStandardsController(IGovernanceStandardsApplication standards) : ControllerBase
{
    private const int MaxText = 400_000;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? area, [FromQuery] string? status, [FromQuery] string? topic,
        [FromQuery] string? repository, [FromQuery] string? module, [FromQuery] string? search, CancellationToken ct) =>
        Ok(await standards.ListAsync(new StandardFilter
        {
            Area = area, Status = status, Topic = topic, Repository = repository, Module = module, Search = search
        }, ct));

    /// <summary>Markdown com os padrões que valem para o card (repositórios do card; <c>?repository=</c> restringe).</summary>
    [HttpGet("for-card/{card}")]
    public async Task<IActionResult> ForCard(string card, [FromQuery] string? repository, [FromServices] WorkItemToolsService tools, CancellationToken ct)
    {
        var repos = repository is null ? await tools.CardRepositoriesAsync(card.TrimStart('#'), ct) : [];
        var md = await standards.ForCardMarkdownAsync(repository, repos, null, ct);
        return Content(md, "text/markdown; charset=utf-8");
    }

    [HttpPost("check")]
    public async Task<IActionResult> Check([FromBody] StandardCheckInput input, CancellationToken ct)
    {
        if (input.Text is { Length: > MaxText }) return BadRequest(new { message = $"Texto maior que {MaxText / 1000} KB." });
        var caller = GovernanceCaller.From(HttpContext);
        var source = caller.Source is GovernanceSource.Executor or GovernanceSource.Specialist ? caller.Source : input.Source;
        var r = await standards.CheckAsync(input with { Source = source }, caller.Actor, ct);
        return Ok(new { decision = r.Decision, mode = r.Mode, wouldDecide = r.WouldDecide, findings = r.Findings });
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public Task<IActionResult> Create([FromBody] StandardUpsert request, CancellationToken ct) =>
        Guarded(async () => Ok(await standards.CreateAsync(request, Actor(), ct)));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "admin")]
    public Task<IActionResult> Update(Guid id, [FromBody] StandardData data, CancellationToken ct) =>
        Guarded(async () => await standards.UpdateAsync(id, data, Actor(), ct) is { } s ? Ok(s) : NotFound());

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await standards.DeleteAsync(id, Actor(), ct) switch
        {
            null => NotFound(),
            false => Conflict(new { message = "Padrão importado não se apaga — descontinue (ou reimporte a origem)." }),
            true => NoContent()
        };

    public sealed record WikiImportRequest(string Url);

    /// <summary>Lê a página da wiki e as subpáginas pela integração DevOps de quem pede e monta o rascunho.</summary>
    [HttpPost("import/wiki")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ImportWiki([FromBody] WikiImportRequest request, [FromServices] IAzureService azure, CancellationToken ct)
    {
        try
        {
            var pages = await azure.GetWikiPagesAsync(request.Url, ct);
            var wiki = pages.Select(p => new WikiPage(p.Path, p.Content, p.Url ?? request.Url)).ToList();
            var parsed = WikiStandardsParser.Parse(wiki);
            if (parsed.Count == 0) return UnprocessableEntity(new { message = "Nenhuma regra com ID (tabela | ID | EN | PT |) encontrada na página." });
            var draft = await standards.CreateDraftAsync("wiki", WikiStandardsParser.Version(wiki), request.Url, parsed, replacesSource: true, Actor(), ct);
            return Ok(Summary(draft, pages.Count));
        }
        catch (DevOpsActionException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    public sealed record ReverseImportRequest(List<string>? Modules);

    /// <summary>Propõe padrões de aplicação a partir da engenharia reversa publicada (ADR, TEC e o "Onde" do índice técnico).</summary>
    [HttpPost("import/reverse-engineering")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ImportReverse([FromBody] ReverseImportRequest request, [FromServices] IKnowledgeRepository knowledge, CancellationToken ct)
    {
        var modules = request.Modules?.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim().ToLowerInvariant()).ToHashSet();
        var items = new List<ReverseStandardSource>();
        foreach (var kind in ReverseStandardsGenerator.Kinds)
            foreach (var e in await knowledge.GetIndexEntriesByKindAsync(kind, ct))
                if (!e.Removed && (modules is null || modules.Count == 0 || modules.Contains(e.ModuleKey.ToLowerInvariant())))
                    items.Add(new ReverseStandardSource(e.ModuleKey, e.ItemId, e.Kind, e.Title, e.Body, e.Evidence));
        var parsed = ReverseStandardsGenerator.Generate(items);
        if (parsed.Count == 0) return UnprocessableEntity(new { message = "A engenharia reversa publicada não tem ADR, TEC nem índice técnico para esses módulos." });
        // Só descontinua o que sumiu quando a importação cobre todos os módulos.
        var draft = await standards.CreateDraftAsync("reverse-engineering", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"), null, parsed,
            replacesSource: modules is null || modules.Count == 0, Actor(), ct);
        return Ok(Summary(draft, items.Select(i => i.Module).Distinct().Count()));
    }

    [HttpGet("import/{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Draft(Guid id, CancellationToken ct) =>
        await standards.GetDraftAsync(id, ct) is { } d ? Ok(Summary(d, null)) : NotFound();

    [HttpPost("import/{id:guid}/apply")]
    [Authorize(Roles = "admin")]
    public Task<IActionResult> Apply(Guid id, [FromBody] DraftApply request, CancellationToken ct) =>
        Guarded(async () => await standards.ApplyDraftAsync(id, request, Actor(), ct) is { } r
            ? Ok(new { added = r.Added, changed = r.Changed, removed = r.Removed })
            : NotFound());

    /// <summary>Rascunho no formato da tela: novos como padrão, alterados como {before, after}, removidos pela chave.</summary>
    private static object Summary(GovernanceStandardDraft d, int? sources) => new
    {
        d.Id, d.Kind, d.Version, d.Url, d.Status, d.CreatedAt, d.CreatedBy, sources,
        added = d.Items.Where(i => i.Change == "added" && i.Data is not null).Select(i => AsStandard(i.Key, i.Data!)).ToList(),
        changed = d.Items.Where(i => i.Change == "changed" && i.Data is not null).Select(i => new
        {
            before = new { key = i.Key, code = i.Data!.Code, area = i.Data.Area, topic = i.Data.Topic, title = i.PreviousTitle, textPt = i.PreviousTextPt, status = i.PreviousStatus },
            after = AsStandard(i.Key, i.Data)
        }).ToList(),
        removed = d.Items.Where(i => i.Change == "removed").Select(i => i.Key).ToList(),
        unchanged = d.Items.Count(i => i.Change == "same")
    };

    private static object AsStandard(string key, StandardData x) => new
    {
        key, code = x.Code, area = x.Area, topic = x.Topic, title = x.Title, textPt = x.TextPt, textEn = x.TextEn,
        examples = x.Examples, appliesTo = x.AppliesTo, source = x.Source, status = x.Status, effect = x.Effect, check = x.Check, order = x.Order
    };

    private solvace.governance.application.Contracts.GovernanceActor Actor() => GovernanceCaller.From(HttpContext).Actor;

    private async Task<IActionResult> Guarded(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
