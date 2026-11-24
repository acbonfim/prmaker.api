using System.Text;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.infra.Pdf;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0099: markdown → PDF no servidor (Markdig + PDFsharp-MigraDoc). Sem rede; fontes do sistema ou a embutida.</summary>
public class MarkdownPdfRenderer0099Tests
{
    /// <summary>PNG 1×1 vermelho.</summary>
    private const string Png1x1 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==";

    public static readonly string Sample = $"""
        # Relatório de bugs — 5S

        Resumo **executivo** com *itálico*, `código inline`, [link](https://dev.azure.com/solvace) e acentuação: ação, médio, Ç.
        Linha com emoji 🚀 que a fonte não tem.

        ## Números

        | Estado | Quantidade | % |
        |:---|---:|:---:|
        | Active | 42 | 30% |
        | Resolved | 98 | 70% |

        - Item 1
          - Sub-item **a**
            1. Terceiro nível
        - Item 2
        - [x] Tarefa feita
        - [ ] Tarefa aberta

        1. Primeiro
        2. Segundo

        > Citação com **negrito**.

        ```sql
        SELECT [System.Id] FROM WorkItems
            WHERE [System.State] = 'Active'
        ```

        ---

        ![pixel](data:image/png;base64,{Png1x1})

        ![print do item](anexo:print.png)

        ![sem arquivo](nao-existe.png)
        """;

    private static readonly MarkdownPdfRenderer Renderer = new();

    [Fact]
    public void Renders_a_real_pdf()
    {
        var pdf = Renderer.Render(Sample, new PdfRenderOptions
        {
            Title = "Relatório de bugs — 5S",
            Subtitle = "Épico E1234 · item #5678 — Relatório",
            Date = DateTimeOffset.Parse("2026-11-24T12:00:00Z"),
            Images = new Dictionary<string, PdfImage> { ["anexo:print.png"] = new(Convert.FromBase64String(Png1x1), "image/png") }
        });
        Assert.True(pdf.Length > 2_000, $"PDF pequeno demais: {pdf.Length} bytes");
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(pdf, Math.Max(0, pdf.Length - 64), Math.Min(64, pdf.Length)));

        // Para conferir à mão: PDF_SAMPLE_OUT=/tmp/x.pdf dotnet test --filter MarkdownPdfRenderer0099Tests
        if (Environment.GetEnvironmentVariable("PDF_SAMPLE_OUT") is { Length: > 0 } path)
            File.WriteAllBytes(path, pdf);
    }

    [Fact]
    public void Long_document_has_several_pages()
    {
        var sb = new StringBuilder("# Longo\n\n");
        for (var i = 0; i < 400; i++) sb.AppendLine($"Parágrafo {i} com algum texto para ocupar a linha inteira do documento gerado.\n");
        var pdf = Renderer.Render(sb.ToString());
        Assert.True(CountPages(pdf) > 3, "deveria quebrar em várias páginas");
    }

    [Fact]
    public void Empty_markdown_still_renders()
    {
        var pdf = Renderer.Render(string.Empty, new PdfRenderOptions { Title = "Vazio" });
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Rejects_huge_markdown() =>
        Assert.Throws<DomainException>(() => Renderer.Render(new string('a', IMarkdownPdfRenderer.MaxMarkdownLength + 1)));

    [Fact]
    public void Lists_only_non_data_image_references() =>
        Assert.Equal(["anexo:print.png", "nao-existe.png"], Renderer.ImageReferences(Sample));

    [Fact]
    public void Fonts_are_resolved() => Assert.Contains("sans:", MarkdownPdfRenderer.EnsureFonts());

    private static int CountPages(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using var doc = PdfSharp.Pdf.IO.PdfReader.Open(stream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }
}
