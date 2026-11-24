using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.executionplans.application.Contracts;

namespace solvace.prform.Controllers;

/// <summary>0099: corpo do <c>POST Pdf/render</c>.</summary>
public class RenderPdfRequest
{
    public string? Title { get; set; }
    public string? Markdown { get; set; }
    /// <summary>Nome do arquivo baixado (padrão: pelo título).</summary>
    public string? Name { get; set; }
}

/// <summary>
/// 0099: markdown → PDF para baixar ("Baixar PDF" do visualizador de arquivos). Nada é gravado; só imagens <c>data:</c>
/// (sem rede). Para gravar no item use <c>POST WorkItems/{n}/files/pdf</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class PdfController(IMarkdownPdfRenderer renderer, ILogger<PdfController> logger) : ControllerBase
{
    private const long MaxRequestBytes = IMarkdownPdfRenderer.MaxMarkdownLength * 2L + 1024 * 1024;

    [HttpPost("render")]
    [RequestSizeLimit(MaxRequestBytes)]
    public ActionResult Render([FromBody] RenderPdfRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Markdown))
            return BadRequest(new { error = "Informe o markdown." });
        try
        {
            var data = renderer.Render(request.Markdown, new PdfRenderOptions { Title = request.Title });
            return File(data, "application/pdf", solvace.prform.Execution.WorkItemPdfService.FileName(request.Name, request.Title));
        }
        catch (solvace.executionplans.domain.Entities.DomainException e)
        {
            return BadRequest(new { error = e.Message });
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            logger.LogWarning(e, "Falha ao gerar o PDF");
            return UnprocessableEntity(new { error = $"Não consegui gerar o PDF: {e.Message}" });
        }
    }
}
