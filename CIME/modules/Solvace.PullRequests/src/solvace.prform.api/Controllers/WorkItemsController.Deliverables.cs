using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using solvace.prform.Execution;

namespace solvace.prform.Controllers;

/// <summary>
/// 0091: entregas do item — o que cada sub-tarefa gerou (PR, branch, código com o diff, chamado, DevOps, Timeline...).
/// A tela lê; a skill grava (o código pelo <c>prmake-plan.sh deliver-code</c>, com o diff do worktree).
/// </summary>
public partial class WorkItemsController
{
    /// <summary>Diff de até 2 MB + folga do multipart (o que passa fica cortado na gravação).</summary>
    private const long MaxDeliverableRequestBytes = WorkItemDeliverable.MaxPatchBytes * 3L;

    private static readonly JsonSerializerOptions DeliverableJson = new(JsonSerializerDefaults.Web);

    /// <summary>Entregas do item (gravadas + as tiradas dos registros do card), na ordem das sub-tarefas.</summary>
    [HttpGet("{number:long}/deliverables")]
    public Task<ActionResult<List<WorkDeliverableResponse>>> ListDeliverables([FromRoute] long number,
        [FromServices] IWorkItemDeliverableApplication deliverables, CancellationToken ct) =>
        RunItem<List<WorkDeliverableResponse>>(async () =>
        {
            await EnsureSpecialistItemAsync(number, ct);
            return Ok(await deliverables.ListAsync(number, ct));
        });

    /// <summary>Diff da entrega: fotografia do código ou compare ao vivo no GitHub (PR/branch). Sem diff → 404.</summary>
    [HttpGet("{number:long}/deliverables/{id}/diff")]
    public Task<ActionResult<WorkDeliverableDiffResponse>> DeliverableDiff([FromRoute] long number, [FromRoute] string id,
        [FromServices] IWorkItemDeliverableApplication deliverables, CancellationToken ct) =>
        RunItem<WorkDeliverableDiffResponse>(async () =>
        {
            await EnsureSpecialistItemAsync(number, ct);
            return Ok(await deliverables.DiffAsync(number, Uri.UnescapeDataString(id), ct));
        });

    /// <summary>Grava uma entrega na sub-tarefa (mesma chave atualiza). Body <see cref="WorkDeliverableInput"/>.</summary>
    [HttpPost("tasks/{taskNumber:long}/deliverables")]
    public Task<ActionResult<WorkDeliverableResponse>> RecordDeliverable([FromRoute] long taskNumber, [FromBody] WorkDeliverableInput input,
        [FromServices] IWorkItemDeliverableApplication deliverables, CancellationToken ct) =>
        RunItem<WorkDeliverableResponse>(async () =>
        {
            EnsureNotSpecialist();
            return StatusCode(StatusCodes.Status201Created, await deliverables.RecordAsync(taskNumber, input, await ActorAsync(ct), ct));
        });

    /// <summary>
    /// Código alterado da sub-tarefa (multipart): <c>repository</c>, <c>branch</c>, <c>base</c>, <c>mergeBase</c>, <c>head</c>,
    /// <c>commits</c> (JSON <c>[{sha,title}]</c>) e o arquivo <c>patch</c> (<c>git diff</c>). Mesmo repositório + branch substitui.
    /// </summary>
    [HttpPost("tasks/{taskNumber:long}/deliverables/code")]
    [RequestSizeLimit(MaxDeliverableRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxDeliverableRequestBytes)]
    public Task<ActionResult<WorkDeliverableResponse>> RecordCode([FromRoute] long taskNumber,
        [FromServices] IWorkItemDeliverableApplication deliverables, CancellationToken ct) =>
        RunItem<WorkDeliverableResponse>(async () =>
        {
            EnsureNotSpecialist();
            var (upload, error) = await ReadCodeUploadAsync(ct);
            if (upload is null)
                return BadRequest(new { error });
            return StatusCode(StatusCodes.Status201Created, await deliverables.RecordCodeAsync(taskNumber, upload, await ActorAsync(ct), ct));
        });

    private void EnsureNotSpecialist()
    {
        if (CaseClaims.SpecialistOf(User) is not null)
            throw new ExecutionForbiddenException("As entregas são gravadas pela execução da sub-tarefa, não pelo especialista.");
    }

    /// <summary>Lê o multipart do <c>deliver-code</c> (compartilhado com a rota pela etapa do plano).</summary>
    internal async Task<(WorkDeliverableCodeUpload? Upload, string? Error)> ReadCodeUploadAsync(CancellationToken ct) =>
        await ReadCodeUploadAsync(Request, ct);

    internal static async Task<(WorkDeliverableCodeUpload? Upload, string? Error)> ReadCodeUploadAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType)
            return (null, "Envie multipart/form-data com repository, branch, base, head, commits e o arquivo 'patch'.");
        var form = await request.ReadFormAsync(ct);
        var repository = form["repository"].ToString().Trim();
        var branch = form["branch"].ToString().Trim();
        if (repository.Length == 0 || branch.Length == 0)
            return (null, "Informe repository e branch.");

        List<WorkDeliverableCommit> commits = [];
        var commitsJson = form["commits"].ToString();
        if (!string.IsNullOrWhiteSpace(commitsJson))
        {
            try
            {
                commits = (JsonSerializer.Deserialize<List<CommitRow>>(commitsJson, DeliverableJson) ?? [])
                    .Where(c => !string.IsNullOrWhiteSpace(c.Sha))
                    .Select(c => new WorkDeliverableCommit(c.Sha!.Trim(), c.Title))
                    .ToList();
            }
            catch (JsonException)
            {
                return (null, "commits inválido: use JSON [{\"sha\":\"...\",\"title\":\"...\"}].");
            }
        }

        byte[] patch = [];
        if ((form.Files.GetFile("patch") ?? form.Files.FirstOrDefault()) is { Length: > 0 } file)
        {
            using var buffer = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
            await file.CopyToAsync(buffer, ct);
            patch = buffer.ToArray();
        }

        string? Opt(string name) => string.IsNullOrWhiteSpace(form[name].ToString()) ? null : form[name].ToString().Trim();
        return (new WorkDeliverableCodeUpload(repository, branch, Opt("base"), Opt("mergeBase"), Opt("head"), commits, patch), null);
    }

    private sealed class CommitRow
    {
        public string? Sha { get; set; }
        public string? Title { get; set; }
    }
}
