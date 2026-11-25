using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using solvace.knowledge.application;

namespace solvace.prform.Controllers;

public class BaseAskExecutorRequest
{
    public string Question { get; set; } = string.Empty;
    /// <summary>"Analisar a fundo": mais esforço e pode confirmar no código da máquina.</summary>
    public bool Deep { get; set; }
    public List<solvace.prform.Knowledge.ArchitectureAskAttachment> Attachments { get; set; } = [];
}

/// <summary>
/// 0102: Pergunte à Base Solvace pelo executor ("Minhas integrações" → Perguntas da Base Solvace = Executor): a pergunta vira
/// um pedido na fila da máquina de quem perguntou; o Claude Code consulta a Base pelo MCP e grava a resposta
/// (<c>prmake_base_answer</c>). A tela acompanha por <c>execplan:{key}</c> e por este GET.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Architecture/ask/executor")]
[Authorize]
public class BaseAskController(IExecutionQueueApplication queue, solvace.timeline.application.Contracts.IUserRepository users)
    : ExecutionControllerBase(users)
{
    private const string DefaultAttachmentQuestion = "O que é isso e como resolvo?";

    [HttpPost]
    [RequestSizeLimit(24_000_000)] // 0072: até 12 MB de anexos em base64
    public Task<ActionResult<BaseQuestionResponse>> Ask([FromBody] BaseAskExecutorRequest request, CancellationToken ct) =>
        Run<BaseQuestionResponse>(async () =>
        {
            AskAttachmentsChecked attached;
            try
            {
                attached = AskAttachmentRules.Validate(request.Attachments.Select(a => new AskAttachmentInput(a.Name, a.MediaType, a.Data)).ToList());
            }
            catch (solvace.knowledge.domain.Entities.DomainException e)
            {
                return BadRequest(new { error = e.Message });
            }
            var q = (request.Question ?? string.Empty).Trim();
            if (q.Length < 3 && attached.Attachments.Count > 0) q = DefaultAttachmentQuestion;
            if (q.Length < 3) return BadRequest(new { error = "Escreva a pergunta." });
            if (q.Length > solvace.prform.Knowledge.ArchitectureAskService.MaxQuestionChars) q = q[..solvace.prform.Knowledge.ArchitectureAskService.MaxQuestionChars];
            try
            {
                return Ok(await queue.AskBaseAsync(await GetActorAsync(ct), q, request.Deep, Files(attached.Attachments), ct));
            }
            catch (BaseAskUnavailableException e)
            {
                return Conflict(new { code = e.Code, error = e.Message });
            }
        });

    [HttpGet("{id:guid}")]
    public Task<ActionResult<BaseQuestionResponse>> Get([FromRoute] Guid id, CancellationToken ct) =>
        Run<BaseQuestionResponse>(async () => Ok(await queue.GetBaseQuestionAsync(id, CurrentUserId, ct)));

    [HttpPost("{id:guid}/cancel")]
    public Task<ActionResult<BaseQuestionResponse>> Cancel([FromRoute] Guid id, CancellationToken ct) =>
        Run<BaseQuestionResponse>(async () => Ok(await queue.CancelBaseQuestionAsync(id, await GetActorAsync(ct), ct)));

    /// <summary>Anexos validados com nome seguro e único (o executor grava cada um com este nome na pasta do pedido).</summary>
    public static List<BaseQuestionFile> Files(IReadOnlyList<AskAttachment> attachments)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<BaseQuestionFile>();
        for (var i = 0; i < attachments.Count; i++)
        {
            var a = attachments[i];
            var name = SafeName(a.Name, a.MediaType, i + 1);
            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            for (var n = 2; !used.Add(name); n++) name = $"{stem}-{n}{ext}";
            files.Add(new BaseQuestionFile(name, a.MediaType, Convert.ToBase64String(a.Data)));
        }
        return files;
    }

    public static string SafeName(string? name, string mediaType, int index)
    {
        var raw = Path.GetFileName((name ?? string.Empty).Replace('\\', '/'));
        var clean = new string(raw.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray()).Trim('.', '_');
        if (clean.Length > 80) clean = clean[^80..];
        if (clean.Length == 0) clean = $"anexo-{index}";
        if (!Path.HasExtension(clean))
            clean += mediaType switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "application/pdf" => ".pdf",
                _ => ".txt"
            };
        return clean;
    }
}
