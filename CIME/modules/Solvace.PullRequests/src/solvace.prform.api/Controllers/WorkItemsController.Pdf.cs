using Microsoft.AspNetCore.Mvc;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;

namespace solvace.prform.Controllers;

/// <summary>0099: corpo do <c>POST WorkItems/{n}/files/pdf</c>.</summary>
public class WorkItemPdfRequest
{
    /// <summary>Nome do arquivo (ganha <c>.pdf</c> se faltar).</summary>
    public string? Name { get; set; }
    /// <summary>Título do PDF (cabeçalho e capa); vazio = o primeiro título do markdown.</summary>
    public string? Title { get; set; }
    public string? Markdown { get; set; }
    /// <summary>Sub-tarefa do item a que o PDF pertence.</summary>
    public long? TaskNumber { get; set; }
}

/// <summary>
/// 0099: PDF gerado no servidor a partir de um markdown, gravado como arquivo do item (<c>kind = pdf</c>) — imagens citadas
/// por nome (<c>![x](print.png)</c> ou <c>anexo:print.png</c>) vêm dos arquivos do item; <c>data:</c> do próprio markdown.
/// </summary>
public partial class WorkItemsController
{
    /// <summary>Markdown aceito no corpo (JSON escapado pode dobrar).</summary>
    private const long MaxPdfRequestBytes = IMarkdownPdfRenderer.MaxMarkdownLength * 2L + 1024 * 1024;

    /// <summary>
    /// <c>{name, title?, markdown, taskNumber?}</c> → 201 <c>WorkItemFileResponse</c> (kind <c>pdf</c>). Mesmo nome substitui; vale
    /// o limite de arquivo/item dos arquivos do item. O especialista (token do caso) só no item do próprio card/épico.
    /// </summary>
    [HttpPost("{number:long}/files/pdf")]
    [RequestSizeLimit(MaxPdfRequestBytes)]
    public Task<ActionResult<WorkItemFileResponse>> SavePdf([FromRoute] long number, [FromBody] WorkItemPdfRequest request,
        [FromServices] IWorkItemFileApplication files, [FromServices] solvace.prform.Execution.WorkItemPdfService pdf, CancellationToken ct) =>
        RunItem<WorkItemFileResponse>(async () =>
        {
            await EnsureSpecialistItemAsync(number, ct);
            var item = await _items.GetAsync(number, withLogs: false, ct);
            var saved = await pdf.SaveAsync(item, request.Name, request.Title, request.Markdown, request.TaskNumber, await ActorAsync(ct), ct);
            return StatusCode(StatusCodes.Status201Created, saved);
        });
}
