using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using PdfColor = MigraDoc.DocumentObjectModel.Color;

namespace solvace.executionplans.infra.Pdf;

/// <summary>
/// 0099: markdown → PDF (Markdig para a árvore, PDFsharp-MigraDoc 6.2.x — MIT; a 6.5 virou AGPL, não subir). A4, margens de
/// 2 cm, cabeçalho com título · data, capa curta (título/subtítulo/data) e rodapé "Página x de y". Sem rede: imagem só
/// <c>data:</c> ou as que quem chama carregou (<see cref="PdfRenderOptions.Images"/>); o resto vira texto.
/// </summary>
public partial class MarkdownPdfRenderer : IMarkdownPdfRenderer
{
    private const string Sans = SystemFontResolver.Sans;
    private const string Mono = SystemFontResolver.Mono;
    private static readonly double ContentWidthCm = 21.0 - 2 * 2.0;
    private const double IndentStepCm = 0.6;

    private static readonly PdfColor Ink = new(0x22, 0x22, 0x22);
    private static readonly PdfColor Muted = new(0x6b, 0x6b, 0x6b);
    private static readonly PdfColor Accent = new(0x3f, 0x51, 0xb5);
    private static readonly PdfColor Rule = new(0xcc, 0xcc, 0xcc);
    private static readonly PdfColor CodeBack = new(0xf3, 0xf3, 0xf5);
    private static readonly PdfColor HeaderBack = new(0xe8, 0xea, 0xf6);
    private static readonly PdfColor Link = new(0x15, 0x65, 0xc0);

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseTaskLists()
        .UseAutoLinks()
        .Build();

    private static readonly object FontLock = new();
    private static SystemFontResolver? _fonts;

    public MarkdownPdfRenderer() => EnsureFonts();

    /// <summary>Fontes do sistema (uma vez por processo — o PDFsharp só aceita um resolvedor global).</summary>
    public static string EnsureFonts()
    {
        lock (FontLock)
        {
            if (_fonts is null)
            {
                _fonts = new SystemFontResolver();
                if (GlobalFontSettings.FontResolver is null)
                    GlobalFontSettings.FontResolver = _fonts;
            }
            return _fonts.Describe();
        }
    }

    public IReadOnlyList<string> ImageReferences(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        var doc = Markdig.Markdown.Parse(markdown, Pipeline);
        return doc.Descendants<LinkInline>()
            .Where(l => l.IsImage && !string.IsNullOrWhiteSpace(l.Url) && !l.Url!.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Url!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public byte[] Render(string markdown, PdfRenderOptions? options = null)
    {
        markdown ??= string.Empty;
        if (markdown.Length > IMarkdownPdfRenderer.MaxMarkdownLength)
            throw new DomainException($"O markdown passa do limite de {IMarkdownPdfRenderer.MaxMarkdownLength / 1_000_000} milhões de caracteres para o PDF.");
        options ??= new PdfRenderOptions();
        var title = Clean(options.Title) ?? FirstHeading(markdown) ?? "Documento";
        var date = (options.Date ?? DateTimeOffset.UtcNow).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        var document = new Document();
        document.Info.Title = Safe(title);
        document.Info.Author = "PRMake";
        document.Info.Subject = Safe(options.Subtitle ?? string.Empty);
        DefineStyles(document);

        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(2.2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2.0);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2.0);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2.0);
        section.PageSetup.HeaderDistance = Unit.FromCentimeter(1.0);
        section.PageSetup.FooterDistance = Unit.FromCentimeter(1.0);

        var header = section.Headers.Primary.AddParagraph();
        header.Style = "HeaderFooter";
        header.AddText(Safe(Cut(title, 90)) + "  ·  " + date);
        header.Format.Borders.Bottom.Width = 0.5;
        header.Format.Borders.Bottom.Color = Rule;

        var footer = section.Footers.Primary.AddParagraph();
        footer.Style = "HeaderFooter";
        footer.Format.Alignment = ParagraphAlignment.Right;
        footer.AddText("Página ");
        footer.AddPageField();
        footer.AddText(" de ");
        footer.AddNumPagesField();

        // Capa curta: título, subtítulo e data.
        var cover = section.AddParagraph(Safe(title), "DocTitle");
        if (Clean(options.Subtitle) is { } subtitle)
            section.AddParagraph(Safe(subtitle), "DocSubtitle");
        var when = section.AddParagraph("Gerado pelo PRMake em " + date, "DocSubtitle");
        when.Format.SpaceAfter = Unit.FromPoint(14);
        when.Format.Borders.Bottom.Width = 0.75;
        when.Format.Borders.Bottom.Color = Accent;
        _ = cover;

        var ast = Markdig.Markdown.Parse(markdown, Pipeline);
        var ctx = new Ctx(section, options.Images ?? new Dictionary<string, PdfImage>(), 0, false);
        // O primeiro título igual ao título do documento já está na capa.
        var skipFirst = ast.FirstOrDefault() is HeadingBlock { Level: 1 } h1 && string.Equals(InlineText(h1.Inline).Trim(), title.Trim(), StringComparison.Ordinal);
        foreach (var block in ast.Skip(skipFirst ? 1 : 0))
            RenderBlock(block, ctx);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var ms = new MemoryStream();
        renderer.PdfDocument.Save(ms, false);
        return ms.ToArray();
    }

    // ── estilos ──────────────────────────────────────────────────────────────────────────────────

    private static void DefineStyles(Document doc)
    {
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = Sans;
        normal.Font.Size = 10;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
        normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Multiple;
        normal.ParagraphFormat.LineSpacing = 1.15;

        double[] sizes = [18, 15, 13, 11.5, 10.5, 10];
        for (var i = 1; i <= 6; i++)
        {
            var h = doc.Styles[$"Heading{i}"]!;
            h.BaseStyle = StyleNames.Normal;
            h.Font.Size = sizes[i - 1];
            h.Font.Bold = true;
            h.Font.Color = i <= 2 ? Accent : Ink;
            h.ParagraphFormat.SpaceBefore = Unit.FromPoint(i <= 2 ? 12 : 8);
            h.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);
            h.ParagraphFormat.KeepWithNext = true;
        }

        var title = doc.Styles.AddStyle("DocTitle", StyleNames.Normal);
        title.Font.Size = 20;
        title.Font.Bold = true;
        title.Font.Color = Accent;
        title.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);

        var subtitle = doc.Styles.AddStyle("DocSubtitle", StyleNames.Normal);
        subtitle.Font.Size = 9.5;
        subtitle.Font.Color = Muted;
        subtitle.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);

        var hf = doc.Styles.AddStyle("HeaderFooter", StyleNames.Normal);
        hf.Font.Size = 8;
        hf.Font.Color = Muted;

        var code = doc.Styles.AddStyle("Code", StyleNames.Normal);
        code.Font.Name = Mono;
        code.Font.Size = 8.5;
        code.ParagraphFormat.LineSpacing = 1.0;
        code.ParagraphFormat.Shading.Color = CodeBack;
        code.ParagraphFormat.Borders.Width = 0.5;
        code.ParagraphFormat.Borders.Color = Rule;
        code.ParagraphFormat.Borders.Distance = Unit.FromPoint(4);
        code.ParagraphFormat.SpaceBefore = Unit.FromPoint(4);
        code.ParagraphFormat.SpaceAfter = Unit.FromPoint(8);

        var quote = doc.Styles.AddStyle("Quote", StyleNames.Normal);
        quote.Font.Italic = true;
        quote.Font.Color = Muted;
        quote.ParagraphFormat.Borders.Left.Width = 2;
        quote.ParagraphFormat.Borders.Left.Color = Rule;
        quote.ParagraphFormat.Borders.Distance = Unit.FromPoint(6);

        var cell = doc.Styles.AddStyle("Cell", StyleNames.Normal);
        cell.Font.Size = 9;
        cell.ParagraphFormat.SpaceAfter = Unit.FromPoint(0);
        cell.ParagraphFormat.LineSpacing = 1.0;
    }

    // ── blocos ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Onde escrever: a seção (ou célula), as imagens, o recuo de lista e se está numa citação.</summary>
    private sealed record Ctx(Section Section, IReadOnlyDictionary<string, PdfImage> Images, double IndentCm, bool Quote)
    {
        public Paragraph NewParagraph(string? style = null)
        {
            var p = Section.AddParagraph();
            p.Style = style ?? (Quote ? "Quote" : StyleNames.Normal);
            if (IndentCm > 0) p.Format.LeftIndent = Unit.FromCentimeter(IndentCm);
            return p;
        }
    }

    private void RenderBlock(Block block, Ctx ctx)
    {
        switch (block)
        {
            case HeadingBlock h:
            {
                var p = ctx.NewParagraph($"Heading{Math.Clamp(h.Level, 1, 6)}");
                RenderInlines(p, h.Inline, ctx, Fmt.None);
                break;
            }
            case ParagraphBlock para:
            {
                var p = ctx.NewParagraph();
                RenderInlines(p, para.Inline, ctx, Fmt.None);
                break;
            }
            case ListBlock list:
                RenderList(list, ctx);
                break;
            case CodeBlock code: // inclui FencedCodeBlock
                RenderCode(code, ctx);
                break;
            case QuoteBlock quote:
                foreach (var child in quote)
                    RenderBlock(child, ctx with { Quote = true });
                break;
            case ThematicBreakBlock:
            {
                var p = ctx.NewParagraph();
                p.Format.Borders.Bottom.Width = 0.75;
                p.Format.Borders.Bottom.Color = Rule;
                p.Format.SpaceAfter = Unit.FromPoint(8);
                p.Format.Font.Size = 2;
                break;
            }
            case MdTable table:
                RenderTable(table, ctx);
                break;
            case HtmlBlock html:
            {
                var text = HtmlTags().Replace(html.Lines.ToString(), " ").Trim();
                if (text.Length > 0)
                    ctx.NewParagraph().AddText(Safe(System.Net.WebUtility.HtmlDecode(text)));
                break;
            }
            case LinkReferenceDefinitionGroup:
            case BlankLineBlock:
                break;
            case ContainerBlock container:
                foreach (var child in container)
                    RenderBlock(child, ctx);
                break;
            case LeafBlock leaf when leaf.Inline is not null:
            {
                var p = ctx.NewParagraph();
                RenderInlines(p, leaf.Inline, ctx, Fmt.None);
                break;
            }
        }
    }

    private void RenderList(ListBlock list, Ctx ctx)
    {
        var number = list.IsOrdered && int.TryParse(list.OrderedStart, out var start) ? start : 1;
        var indent = ctx.IndentCm + IndentStepCm;
        foreach (var child in list)
        {
            if (child is not ListItemBlock item) continue;
            var marker = list.IsOrdered ? $"{number++}." : (ctx.IndentCm / IndentStepCm % 2 < 1 ? "•" : "◦");
            var first = true;
            foreach (var inner in item)
            {
                if (first && inner is ParagraphBlock para)
                {
                    var p = ctx.NewParagraph();
                    p.Format.LeftIndent = Unit.FromCentimeter(indent);
                    p.Format.FirstLineIndent = Unit.FromCentimeter(-0.45);
                    p.Format.SpaceAfter = Unit.FromPoint(2);
                    p.AddText(marker + " ");
                    RenderInlines(p, para.Inline, ctx, Fmt.None);
                }
                else
                {
                    RenderBlock(inner, ctx with { IndentCm = indent });
                }
                first = false;
            }
        }
        // respiro depois da lista de primeiro nível
        if (ctx.IndentCm == 0) ctx.NewParagraph().Format.SpaceAfter = Unit.FromPoint(2);
    }

    private static void RenderCode(CodeBlock code, Ctx ctx)
    {
        var p = ctx.NewParagraph("Code");
        var lines = code.Lines.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) p.AddLineBreak();
            var line = lines[i].Replace("\t", "    ");
            var lead = line.Length - line.TrimStart(' ').Length;
            // recuo do código: espaço comum é engolido no começo da linha
            p.AddText(Safe(new string(' ', lead) + line[lead..]));
        }
        if (lines.Length == 0 || (lines.Length == 1 && lines[0].Length == 0)) p.AddText(" ");
    }

    private void RenderTable(MdTable md, Ctx ctx)
    {
        var rows = md.OfType<MdTableRow>().ToList();
        var columns = Math.Max(md.ColumnDefinitions.Count, rows.Count == 0 ? 0 : rows.Max(r => r.Count));
        if (columns == 0) return;
        var available = ContentWidthCm - ctx.IndentCm;
        var table = ctx.Section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Rule;
        table.Format.Font.Size = columns > 5 ? 8 : 9;
        table.LeftPadding = Unit.FromPoint(3);
        table.RightPadding = Unit.FromPoint(3);
        table.TopPadding = Unit.FromPoint(2);
        table.BottomPadding = Unit.FromPoint(2);
        if (ctx.IndentCm > 0) table.Rows.LeftIndent = Unit.FromCentimeter(ctx.IndentCm);

        // Largura proporcional ao texto de cada coluna (com mínimo), somando a largura útil.
        var weights = new double[columns];
        for (var c = 0; c < columns; c++)
            weights[c] = Math.Clamp(rows.Select(r => c < r.Count && r[c] is MdTableCell tc ? CellText(tc).Length : 0).DefaultIfEmpty(0).Max(), 4, 60);
        var total = weights.Sum();
        var align = new ParagraphAlignment[columns];
        for (var c = 0; c < columns; c++)
        {
            table.AddColumn(Unit.FromCentimeter(available * weights[c] / total));
            align[c] = c < md.ColumnDefinitions.Count ? md.ColumnDefinitions[c].Alignment switch
            {
                TableColumnAlign.Center => ParagraphAlignment.Center,
                TableColumnAlign.Right => ParagraphAlignment.Right,
                _ => ParagraphAlignment.Left
            } : ParagraphAlignment.Left;
        }

        foreach (var mdRow in rows)
        {
            var row = table.AddRow();
            if (mdRow.IsHeader)
            {
                row.HeadingFormat = true;
                row.Shading.Color = HeaderBack;
                row.Format.Font.Bold = true;
            }
            for (var c = 0; c < columns; c++)
            {
                var p = row.Cells[c].AddParagraph();
                p.Style = "Cell";
                p.Format.Alignment = align[c]; // o estilo do parágrafo venceria o formato da coluna
                if (c < mdRow.Count && mdRow[c] is MdTableCell cell)
                    foreach (var inner in cell)
                        if (inner is LeafBlock { Inline: { } inline })
                            RenderInlines(p, inline, ctx, mdRow.IsHeader ? Fmt.Bold : Fmt.None);
            }
        }
        ctx.Section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(4);
    }

    // ── inlines ──────────────────────────────────────────────────────────────────────────────────

    [Flags]
    private enum Fmt { None = 0, Bold = 1, Italic = 2, Code = 4 }

    private void RenderInlines(Paragraph p, ContainerInline? container, Ctx ctx, Fmt fmt, string? link = null)
    {
        if (container is null) return;
        foreach (var inline in container)
            RenderInline(p, inline, ctx, fmt, link);
    }

    private void RenderInline(Paragraph p, Inline inline, Ctx ctx, Fmt fmt, string? link)
    {
        switch (inline)
        {
            case LiteralInline lit:
                AddRun(p, lit.Content.ToString(), fmt, link);
                break;
            case CodeInline code:
                AddRun(p, code.Content, fmt | Fmt.Code, link);
                break;
            case EmphasisInline em:
            {
                var next = em.DelimiterChar is '*' or '_'
                    ? fmt | (em.DelimiterCount >= 2 ? Fmt.Bold : Fmt.Italic)
                    : fmt; // ~~ e afins: só o texto
                RenderInlines(p, em, ctx, next, link);
                break;
            }
            case LinkInline { IsImage: true } img:
                RenderImage(p, img, ctx);
                break;
            case LinkInline l:
            {
                var url = SafeUrl(l.Url);
                if (l.FirstChild is null) AddRun(p, l.Url ?? string.Empty, fmt, url);
                else RenderInlines(p, l, ctx, fmt, url ?? link);
                break;
            }
            case AutolinkInline auto:
                AddRun(p, auto.Url, fmt, SafeUrl(auto.IsEmail ? "mailto:" + auto.Url : auto.Url));
                break;
            case LineBreakInline br:
                if (br.IsHard) p.AddLineBreak();
                else AddRun(p, " ", fmt, link);
                break;
            case HtmlEntityInline entity:
                AddRun(p, entity.Transcoded.ToString(), fmt, link);
                break;
            case HtmlInline html:
                if (html.Tag.StartsWith("<br", StringComparison.OrdinalIgnoreCase)) p.AddLineBreak();
                break;
            case TaskList task:
                AddRun(p, task.Checked ? "[x] " : "[ ] ", fmt | Fmt.Code, null);
                break;
            case ContainerInline c:
                RenderInlines(p, c, ctx, fmt, link);
                break;
        }
    }

    private static void AddRun(Paragraph p, string text, Fmt fmt, string? link)
    {
        var safe = Safe(text);
        if (safe.Length == 0) return;
        FormattedText run;
        if (link is not null)
        {
            var hyperlink = p.AddHyperlink(link, HyperlinkType.Web);
            run = hyperlink.AddFormattedText(safe);
            run.Color = Link;
            run.Underline = Underline.Single;
        }
        else
        {
            run = p.AddFormattedText(safe);
        }
        if (fmt.HasFlag(Fmt.Bold)) run.Bold = true;
        if (fmt.HasFlag(Fmt.Italic)) run.Italic = true;
        if (fmt.HasFlag(Fmt.Code))
        {
            run.Font.Name = Mono;
            run.Font.Size = 8.5;
            if (link is null) run.Color = new PdfColor(0xad, 0x14, 0x57);
        }
    }

    private void RenderImage(Paragraph p, LinkInline img, Ctx ctx)
    {
        var url = (img.Url ?? string.Empty).Trim();
        var alt = InlineText(img).Trim();
        byte[]? data = null;
        if (DataUri().Match(url) is { Success: true } m && m.Groups["type"].Value is var type && SupportedImage(type))
        {
            try { data = Convert.FromBase64String(m.Groups["data"].Value); }
            catch (FormatException) { data = null; }
        }
        else if (ctx.Images.TryGetValue(url, out var loaded) && SupportedImage(loaded.ContentType))
        {
            data = loaded.Data;
        }

        if (data is null || data.Length == 0)
        {
            AddRun(p, $"[imagem: {(alt.Length > 0 ? alt : Path.GetFileName(url))}]", Fmt.Italic, null);
            return;
        }
        try
        {
            var image = p.AddImage("base64:" + Convert.ToBase64String(data));
            image.LockAspectRatio = true;
            var maxCm = ContentWidthCm - ctx.IndentCm;
            var widthCm = ImageWidthCm(data) is { } natural ? Math.Min(natural, maxCm) : maxCm;
            image.Width = Unit.FromCentimeter(widthCm);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            AddRun(p, $"[imagem ilegível: {(alt.Length > 0 ? alt : "sem nome")}]", Fmt.Italic, null);
        }
    }

    /// <summary>Largura natural (cm, a 96 dpi) de PNG/JPEG pelo cabeçalho — imagem pequena não estica até a margem.</summary>
    private static double? ImageWidthCm(byte[] d)
    {
        int? px = null;
        if (d.Length > 24 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
            px = (d[16] << 24) | (d[17] << 16) | (d[18] << 8) | d[19];
        else if (d.Length > 4 && d[0] == 0xFF && d[1] == 0xD8)
        {
            var i = 2;
            while (i + 9 < d.Length)
            {
                if (d[i] != 0xFF) { i++; continue; }
                var marker = d[i + 1];
                if (marker is >= 0xC0 and <= 0xC3)
                {
                    px = (d[i + 7] << 8) | d[i + 8];
                    break;
                }
                var len = (d[i + 2] << 8) | d[i + 3];
                i += 2 + len;
            }
        }
        return px is > 0 ? px.Value / 96.0 * 2.54 : null;
    }

    private static bool SupportedImage(string? contentType)
    {
        var t = (contentType ?? string.Empty).ToLowerInvariant();
        return t.Contains("png") || t.Contains("jpeg") || t.Contains("jpg") || t.Contains("gif") || t.Contains("bmp");
    }

    // ── util ─────────────────────────────────────────────────────────────────────────────────────

    private static string InlineText(ContainerInline? container)
    {
        if (container is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var inline in container.Descendants<Inline>())
            switch (inline)
            {
                case LiteralInline lit: sb.Append(lit.Content.ToString()); break;
                case CodeInline code: sb.Append(code.Content); break;
                case LineBreakInline: sb.Append(' '); break;
            }
        return sb.ToString();
    }

    private static string CellText(MdTableCell cell) =>
        string.Concat(cell.OfType<LeafBlock>().Select(b => InlineText(b.Inline)));

    private static string? FirstHeading(string markdown) =>
        Markdig.Markdown.Parse(markdown, Pipeline).OfType<HeadingBlock>().FirstOrDefault() is { } h ? Clean(InlineText(h.Inline)) : null;

    /// <summary>Só http(s)/mailto viram link clicável.</summary>
    private static string? SafeUrl(string? url)
    {
        var u = url?.Trim();
        if (string.IsNullOrEmpty(u)) return null;
        return u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? u
            : null;
    }

    /// <summary>
    /// Texto que a fonte desenha: sem caracteres de controle e sem os fora do plano básico (emoji etc. — as fontes do servidor
    /// não têm; viram nada em vez de quadrado).
    /// </summary>
    private static string Safe(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsSurrogate(ch)) continue;
            if (ch is '️' or '‍') continue;
            if (char.IsControl(ch) && ch is not '\t') { sb.Append(' '); continue; }
            sb.Append(ch == '\t' ? ' ' : ch);
        }
        return sb.ToString();
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    [GeneratedRegex(@"^data:(?<type>[a-zA-Z0-9.+/-]+);base64,(?<data>[A-Za-z0-9+/=\s]+)$", RegexOptions.Singleline)]
    private static partial Regex DataUri();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTags();
}
