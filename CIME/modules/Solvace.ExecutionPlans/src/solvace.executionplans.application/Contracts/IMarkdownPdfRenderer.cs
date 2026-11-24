namespace solvace.executionplans.application.Contracts;

/// <summary>Imagem já carregada para o PDF (arquivo do item citado no markdown).</summary>
public record PdfImage(byte[] Data, string ContentType);

/// <summary>
/// 0099: o que vai no PDF além do markdown — título (cabeçalho e capa), subtítulo (épico/card/item) e as imagens que o
/// markdown cita por nome (<c>![alt](print.png)</c> → arquivo do item). Imagens <c>data:</c> vêm do próprio markdown.
/// </summary>
public class PdfRenderOptions
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    /// <summary>Data impressa no cabeçalho (padrão: agora, UTC-3 não — o servidor usa UTC).</summary>
    public DateTimeOffset? Date { get; set; }
    /// <summary>Imagens por nome/URL citado no markdown (sem rede: o que não estiver aqui nem for <c>data:</c> vira texto).</summary>
    public IReadOnlyDictionary<string, PdfImage>? Images { get; set; }
}

/// <summary>
/// 0099: markdown → PDF no servidor (Markdig + PDFsharp-MigraDoc, MIT) — títulos, parágrafos com negrito/itálico/código/links,
/// listas aninhadas, tabelas, blocos de código, citações, regra horizontal e imagens <c>data:</c>/do item; cabeçalho com
/// título e data, rodapé com a página. Sem I/O de rede. Erros de entrada: <see cref="solvace.executionplans.domain.Entities.DomainException"/>.
/// </summary>
public interface IMarkdownPdfRenderer
{
    /// <summary>Limite do markdown aceito (caracteres).</summary>
    const int MaxMarkdownLength = 2_000_000;

    byte[] Render(string markdown, PdfRenderOptions? options = null);

    /// <summary>Referências de imagem do markdown que não são <c>data:</c> (para quem chama carregar dos arquivos do item).</summary>
    IReadOnlyList<string> ImageReferences(string markdown);
}
