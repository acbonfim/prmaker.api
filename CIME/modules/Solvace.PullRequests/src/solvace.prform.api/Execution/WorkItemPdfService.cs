using System.Text.RegularExpressions;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;

namespace solvace.prform.Execution;

/// <summary>
/// 0099: markdown de um item → PDF nos arquivos do item (<c>kind = pdf</c>) — o mesmo caminho no REST
/// (<c>POST WorkItems/{n}/files/pdf</c>) e no MCP (<c>prmake_pdf</c>). Imagens citadas por nome (<c>print.png</c>,
/// <c>anexo:print.png</c>, <c>#3</c>) vêm dos arquivos do item; o resto vira texto. Gerar PDF é CPU: no máximo dois ao mesmo
/// tempo por instância.
/// </summary>
public partial class WorkItemPdfService(IMarkdownPdfRenderer renderer, IWorkItemFileApplication files, ILogger<WorkItemPdfService> logger)
{
    private const int MaxImages = 40;
    private static readonly SemaphoreSlim Gate = new(2, 2);

    public async Task<WorkItemFileResponse> SaveAsync(WorkItemResponse item, string? name, string? title, string? markdown, long? taskNumber,
        ExecutionActor actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            throw new DomainException("Informe o markdown do PDF.");
        if (taskNumber is { } t && item.Tasks.All(x => x.Number != t))
            throw new DomainException($"A sub-tarefa #{t} não é do item #{item.Number}.");

        var images = await LoadImagesAsync(item.Number, renderer.ImageReferences(markdown), ct);
        var owner = EpicKey.IsEpic(item.CardNumber) ? $"Épico {item.CardNumber}" : $"Card {item.CardNumber}";
        var options = new PdfRenderOptions
        {
            Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            Subtitle = $"{owner} · item #{item.Number} — {item.Title}",
            Images = images
        };

        byte[] data;
        await Gate.WaitAsync(ct);
        try
        {
            data = await Task.Run(() => renderer.Render(markdown, options), ct);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            logger.LogWarning(e, "Falha ao gerar o PDF do item #{Item}", item.Number);
            throw new DomainException($"Não consegui gerar o PDF: {e.Message}");
        }
        finally
        {
            Gate.Release();
        }

        var upload = new WorkItemFileUpload(FileName(name, title ?? item.Title), WorkItemFileKind.Pdf, taskNumber, "application/pdf", data);
        return await files.UploadAsync(item.Number, upload, actor, ct);
    }

    /// <summary>Nome do arquivo: o informado (com <c>.pdf</c>) ou o título em slug.</summary>
    public static string FileName(string? name, string? title)
    {
        var n = Path.GetFileName((name ?? string.Empty).Trim().Replace('\\', '/'));
        if (n.Length == 0)
        {
            n = Slug().Replace((title ?? "documento").Trim().ToLowerInvariant(), "-").Trim('-');
            if (n.Length == 0) n = "documento";
            if (n.Length > 80) n = n[..80].Trim('-');
        }
        if (!n.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            n = Regex.Replace(n, @"\.(md|markdown|txt)$", string.Empty, RegexOptions.IgnoreCase) + ".pdf";
        return n;
    }

    /// <summary>Imagens do item citadas no markdown (pela referência exata, para o renderizador achar).</summary>
    private async Task<IReadOnlyDictionary<string, PdfImage>> LoadImagesAsync(long number, IReadOnlyList<string> refs, CancellationToken ct)
    {
        var result = new Dictionary<string, PdfImage>(StringComparer.OrdinalIgnoreCase);
        if (refs.Count == 0) return result;
        var own = (await files.ListAsync(number, ct))
            .Where(f => f.Source == WorkItemFileResponse.SourceItem && f.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var reference in refs.Take(MaxImages))
        {
            var key = Uri.UnescapeDataString(reference.Trim());
            foreach (var prefix in new[] { "anexo:", "arquivo:", "file:", "attachment:" })
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) key = key[prefix.Length..];
            key = key.Trim().TrimStart('#');
            var found = int.TryParse(key, out var n)
                ? own.FirstOrDefault(f => f.Number == n)
                : own.FirstOrDefault(f => string.Equals(f.Name, Path.GetFileName(key), StringComparison.OrdinalIgnoreCase));
            if (found is null) continue;
            try
            {
                var content = await files.GetContentAsync(number, found.Id, ct);
                result[reference] = new PdfImage(content.Data, found.ContentType);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogDebug(e, "Imagem {Ref} do item #{Item} não carregou para o PDF", reference, number);
            }
        }
        return result;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex Slug();
}
