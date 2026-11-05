using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;

namespace solvace.prform.Controllers;

/// <summary>
/// 0085 r5 (1.2): seção "Arquivos" do item de trabalho — os arquivos do item e os do plano ligado
/// (<c>source:"plan"</c>, conteúdo pela rota do plano em <c>contentUrl</c>).
/// </summary>
public partial class WorkItemsController
{
    /// <summary>JSON de <c>POST WorkItems/{n}/files</c> (texto: script, chamado, análise...).</summary>
    public class WorkItemFileJsonRequest
    {
        public string? Name { get; set; }
        /// <summary>script | analysis | ticket | data | attachment | text (vazio = pelo nome).</summary>
        public string? Kind { get; set; }
        public string? Content { get; set; }
        /// <summary>Sub-tarefa do item a que o arquivo pertence.</summary>
        public long? TaskNumber { get; set; }
    }

    /// <summary>Texto em JSON escapado pode dobrar de tamanho; multipart fica no limite do arquivo + folga.</summary>
    private const long MaxItemFileRequestBytes = WorkItemFile.MaxFileBytes * 2 + 1024 * 1024;

    private static readonly JsonSerializerOptions FileJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Arquivos do item (<c>source:"item"</c>) e do plano ligado (<c>source:"plan"</c>, com <c>taskNumber</c> pela etapa),
    /// sem conteúdo; cada um traz <c>contentUrl</c> relativo a <c>api/v1/</c>.
    /// </summary>
    [HttpGet("{number:long}/files")]
    public Task<ActionResult<IReadOnlyList<WorkItemFileResponse>>> ListFiles([FromRoute] long number,
        [FromServices] IWorkItemFileApplication files, CancellationToken ct) =>
        RunItem<IReadOnlyList<WorkItemFileResponse>>(async () =>
        {
            await EnsureSpecialistItemAsync(number, ct);
            return Ok(await files.ListAsync(number, ct));
        });

    /// <summary>Conteúdo de um arquivo do item; <c>download=true</c> baixa com o nome original.</summary>
    [HttpGet("{number:long}/files/{fileId:guid}/content")]
    public Task<ActionResult> GetFileContent([FromRoute] long number, [FromRoute] Guid fileId,
        [FromServices] IWorkItemFileApplication files, [FromQuery] bool download = false, CancellationToken ct = default) =>
        RunPlain(async () =>
        {
            await EnsureSpecialistItemAsync(number, ct);
            var file = await files.GetContentAsync(number, fileId, ct);
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (download)
                return File(file.Data, file.File.ContentType, file.File.Name);
            Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(file.File.Name)}";
            return File(file.Data, file.File.ContentType);
        });

    /// <summary>
    /// Grava um arquivo no item: multipart (<c>file</c>, <c>name?</c>, <c>kind?</c>, <c>taskNumber?</c>) ou JSON
    /// <c>{name, kind, content, taskNumber?}</c>. Mesmo tipo + nome substitui; limite de 10 MB por arquivo e 50 MB por item;
    /// no item da análise valem as recusas do plano de análise (script que altera dados, chamado).
    /// </summary>
    [HttpPost("{number:long}/files")]
    [RequestSizeLimit(MaxItemFileRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxItemFileRequestBytes)]
    public Task<ActionResult<WorkItemFileResponse>> UploadFile([FromRoute] long number,
        [FromServices] IWorkItemFileApplication files, CancellationToken ct) =>
        RunItem<WorkItemFileResponse>(async () =>
        {
            await EnsureSpecialistItemAsync(number, ct);
            WorkItemFileUpload upload;
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
                if (file is null || file.Length == 0)
                    return BadRequest(new { error = "Envie o arquivo no campo 'file'." });
                if (file.Length > WorkItemFile.MaxFileBytes)
                    return BadRequest(new { error = $"O arquivo passa do limite de {WorkItemFile.MaxFileBytes / (1024 * 1024)} MB." });
                var name = form["name"].ToString();
                var fileName = string.IsNullOrWhiteSpace(name) ? file.FileName : name;
                using var buffer = new MemoryStream((int)file.Length);
                await file.CopyToAsync(buffer, ct);
                upload = new WorkItemFileUpload(fileName, NullIfEmpty(form["kind"].ToString()), ParseTask(form["taskNumber"].ToString()),
                    ExecutionPlanController.ResolveContentType(fileName, file.ContentType), buffer.ToArray());
            }
            else
            {
                WorkItemFileJsonRequest? body;
                try
                {
                    body = await JsonSerializer.DeserializeAsync<WorkItemFileJsonRequest>(Request.Body, FileJson, ct);
                }
                catch (JsonException)
                {
                    return BadRequest(new { error = "JSON inválido: use {name, kind, content, taskNumber?} ou multipart com 'file'." });
                }
                if (body is null || string.IsNullOrWhiteSpace(body.Name))
                    return BadRequest(new { error = "Informe o nome do arquivo (name)." });
                if (string.IsNullOrEmpty(body.Content))
                    return BadRequest(new { error = "Informe o conteúdo do arquivo (content)." });
                upload = new WorkItemFileUpload(body.Name, body.Kind, body.TaskNumber,
                    ExecutionPlanController.ResolveContentType(body.Name, "text/plain; charset=utf-8"), Encoding.UTF8.GetBytes(body.Content));
            }
            var saved = await files.UploadAsync(number, upload, await ActorAsync(ct), ct);
            return StatusCode(StatusCodes.Status201Created, saved);
        });

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static long? ParseTask(string? value) =>
        long.TryParse(value?.Trim().TrimStart('#'), out var n) && n > 0 ? n : null;
}
