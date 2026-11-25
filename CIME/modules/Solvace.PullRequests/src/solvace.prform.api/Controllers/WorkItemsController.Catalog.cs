using Microsoft.AspNetCore.Mvc;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using solvace.prform.Execution;
using solvace.prform.Skills;

namespace solvace.prform.Controllers;

/// <summary>0085: catálogo dos itens de trabalho — tipos de sub-tarefa, tipos de item e modelos prontos (Skills Configurations).</summary>
public partial class WorkItemsController
{
    /// <summary>
    /// Tipos de sub-tarefa (<c>WorkItemTaskKinds</c>, cada param com <c>input</c>/<c>options?</c>), tipos de item
    /// (<c>itemTypes</c> = <c>WorkItemTypes</c>) e modelos (<c>WorkItemTemplates</c>) preenchidos para o card. r4: cada modelo
    /// volta com <c>applicable</c> (+ <c>reason</c> quando o <c>when</c> não bate), <c>ask[]</c> com <c>options</c> resolvidas
    /// (<c>{promotionTargets}</c> → destinos do perfil do repositório; <c>optionsByRepository</c> por repositório do card),
    /// <c>default</c> renderizado e <c>suggestions</c> (repositórios do card) no ask de repositório. Título/descrição vêm com as
    /// respostas padrão (sem resposta: <c>&lt;rótulo&gt;</c>); as sub-tarefas mantêm <c>{ask:..}</c>/<c>{suffix:@..}</c> sem
    /// resposta padrão para a tela pré-visualizar. <c>{repository}</c> = repositório do PR principal do card
    /// (<c>repository</c>/<c>repositories</c> na resposta). Sem card, os modelos voltam como estão.
    /// Marcadores: <see cref="WorkItemTemplateRenderer"/>.
    /// </summary>
    [HttpGet("catalog")]
    public Task<ActionResult<object>> Catalog([FromQuery] string? card, [FromServices] WorkItemToolsService tools, CancellationToken ct) =>
        Run<object>(async () => Ok(await tools.CatalogAsync(card, ct)));

    /// <summary>
    /// r4: cria o item a partir de um modelo (ferramenta) — renderiza no servidor com as respostas do <c>ask</c>
    /// (<c>required</c> sem resposta nem padrão → 400 <c>{code:"invalidAnswers", missing}</c>; destino de outro tipo de
    /// repositório → 400; modelo que não se aplica ao card → 400 <c>{code:"notApplicable"}</c>; modelo inexistente → 404
    /// <c>{code:"templateNotFound"}</c>). Origem <c>template</c> (<c>specialist</c> com o token do caso). Igual a outro item
    /// do card → 409 <c>{code:"duplicate", existing}</c>, como no create.
    /// </summary>
    [HttpPost("card/{card}/from-template")]
    public Task<ActionResult<WorkItemResponse>> FromTemplate([FromRoute] string card, [FromBody] CreateFromTemplateRequest request,
        [FromServices] WorkItemToolsService tools, CancellationToken ct) =>
        RunItem<WorkItemResponse>(async () =>
        {
            EnsureSpecialistCard(card);
            var c = SkillsRulesService.NormalizeCard(card);
            try
            {
                var create = await tools.BuildCreateRequestAsync(c, request, ct);
                var origin = CaseClaims.SpecialistOf(User) is not null ? WorkItemOrigin.Specialist : WorkItemOrigin.Template;
                var created = await _items.CreateAsync(c, create, origin, await ActorAsync(ct), ct);
                return StatusCode(StatusCodes.Status201Created, created);
            }
            catch (WorkItemTemplateException e) when (e.Code == WorkItemTemplateException.NotFound)
            {
                return NotFound(new { code = e.Code, error = e.Message });
            }
            catch (WorkItemTemplateException e)
            {
                return BadRequest(new { code = e.Code, error = e.Message, missing = e.Missing });
            }
        });
}
