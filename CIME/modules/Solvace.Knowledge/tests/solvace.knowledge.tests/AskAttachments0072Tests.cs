using System.Text;
using solvace.knowledge.application;
using solvace.knowledge.domain.Entities;
using Xunit;

namespace solvace.knowledge.tests;

/// <summary>0072: anexos do Pergunte — tipo conferido pelo conteúdo, limites, texto cortado e contexto do prompt.</summary>
public class AskAttachments0072Tests
{
    private static readonly byte[] PngHead = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static byte[] Png(int size = 64) => PngHead.Concat(new byte[size]).ToArray();
    private static byte[] Pdf(int pages, int size = 64) =>
        Encoding.Latin1.GetBytes("%PDF-1.4\n" + string.Concat(Enumerable.Range(0, pages).Select(i => $"{i} 0 obj << /Type /Page >> endobj\n")) + "1 0 obj << /Type /Pages >> endobj\n").Concat(new byte[size]).ToArray();
    private static AskAttachmentInput In(string name, string? type, byte[] data) => new(name, type, Convert.ToBase64String(data));
    private static AskAttachmentInput Text(string name, string text, string? type = null) => In(name, type, Encoding.UTF8.GetBytes(text));

    [Fact]
    public void No_attachments_is_fine()
    {
        Assert.Empty(AskAttachmentRules.Validate(null).Attachments);
        Assert.Empty(AskAttachmentRules.Validate([]).Attachments);
    }

    [Fact]
    public void Image_type_comes_from_the_content_not_from_what_the_browser_said()
    {
        var ok = AskAttachmentRules.Validate([In("print.png", "text/plain", Png())]).Attachments.Single();
        Assert.Equal("image/png", ok.MediaType);
        Assert.True(ok.IsImage);
        Assert.Equal("image/jpeg", AskAttachmentRules.Validate([In("a.jpg", "", [0xFF, 0xD8, 0xFF, 0xE0, 0, 0])]).Attachments.Single().MediaType);
        Assert.Equal("image/webp", AskAttachmentRules.Validate([In("a.webp", null, Encoding.ASCII.GetBytes("RIFF").Concat(new byte[4]).Concat(Encoding.ASCII.GetBytes("WEBPVP8 ")).ToArray())]).Attachments.Single().MediaType);
        Assert.Equal("image/gif", AskAttachmentRules.Validate([In("a.gif", null, Encoding.ASCII.GetBytes("GIF89a....."))]).Attachments.Single().MediaType);
    }

    [Fact]
    public void Data_url_from_the_browser_is_accepted()
    {
        var input = new AskAttachmentInput("p.png", "image/png", "data:image/png;base64," + Convert.ToBase64String(Png()));
        Assert.Single(AskAttachmentRules.Validate([input]).Attachments);
    }

    [Theory]
    [InlineData("@@não é base64@@")]
    [InlineData("")]
    public void Corrupt_or_empty_data_is_refused_with_the_file_name(string data)
    {
        var e = Assert.Throws<DomainException>(() => AskAttachmentRules.Validate([new AskAttachmentInput("x.png", "image/png", data)]));
        Assert.Contains("x.png", e.Message);
    }

    [Fact]
    public void More_than_four_files_is_refused()
    {
        var five = Enumerable.Range(0, 5).Select(i => In($"{i}.png", null, Png())).ToList();
        Assert.Contains("no máximo 4", Assert.Throws<DomainException>(() => AskAttachmentRules.Validate(five)).Message);
        Assert.Equal(4, AskAttachmentRules.Validate(five.Take(4).ToList()).Attachments.Count);
    }

    [Fact]
    public void Big_image_and_big_total_are_refused()
    {
        Assert.Contains("5 MB", Assert.Throws<DomainException>(() => AskAttachmentRules.Validate([In("grande.png", null, Png(5 * 1024 * 1024))])).Message);
        var three = Enumerable.Range(0, 3).Select(i => In($"{i}.pdf", null, Pdf(1, 4_500_000))).ToList();
        Assert.Contains("12 MB", Assert.Throws<DomainException>(() => AskAttachmentRules.Validate(three)).Message);
    }

    [Fact]
    public void Pdf_pages_are_counted_without_counting_the_pages_tree()
    {
        Assert.Equal(3, AskAttachmentRules.CountPdfPages(Pdf(3)));
        Assert.Single(AskAttachmentRules.Validate([In("ok.pdf", "application/pdf", Pdf(10))]).Attachments);
        Assert.Contains("11 páginas", Assert.Throws<DomainException>(() => AskAttachmentRules.Validate([In("longo.pdf", null, Pdf(11))])).Message);
        Assert.True(AskAttachmentRules.Validate([In("ok.pdf", null, Pdf(2))]).Attachments.Single().IsPdf);
    }

    [Theory]
    [InlineData("erro.log", "application/octet-stream")]
    [InlineData("erro.log", "")]
    [InlineData("dados.csv", null)]
    [InlineData("x.bin", "text/plain")]
    [InlineData("resp.json", "application/json")]
    public void Text_is_accepted_by_declared_type_or_by_extension(string name, string? type) =>
        Assert.True(AskAttachmentRules.Validate([Text(name, "2026-10-23 ERRO NullReference", type)]).Attachments.Single().IsText);

    [Fact]
    public void Unknown_or_binary_files_are_refused()
    {
        Assert.Contains("não é aceito", Assert.Throws<DomainException>(() => AskAttachmentRules.Validate([Text("app.exe", "MZ texto", "application/x-msdownload")])).Message);
        Assert.Contains("binário", Assert.Throws<DomainException>(() => AskAttachmentRules.Validate([In("dump.log", null, [65, 66, 0, 67, 68])])).Message);
    }

    [Fact]
    public void Long_text_is_cut_at_a_character_boundary_with_a_note()
    {
        var text = string.Concat(Enumerable.Repeat("ação ", 40_000));   // > 100 KB, com acentos de 2 bytes
        var result = AskAttachmentRules.Validate([Text("grande.log", text)]);
        var a = result.Attachments.Single();
        Assert.True(a.Data.Length <= AskAttachmentRules.MaxTextBytes);
        var decoded = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(a.Data);   // não parte caractere
        Assert.StartsWith("ação ação", decoded);
        Assert.Contains("grande.log", Assert.Single(result.Notes));
    }

    [Fact]
    public void File_name_loses_path_and_control_characters()
    {
        Assert.Equal("passwd", AskAttachmentRules.Validate([Text("../../etc/passwd", "x", "text/plain")]).Attachments.Single().Name);
        Assert.Equal("anexo", AskAttachmentRules.Validate([Text("  ", "x", "text/plain")]).Attachments.Single().Name);
        Assert.True(AskAttachmentRules.Validate([Text(new string('a', 300) + ".txt", "x", "text/plain")]).Attachments.Single().Name.Length <= 80);
    }

    [Fact]
    public void Plan_prompt_asks_what_the_attachments_show_only_when_there_are_attachments()
    {
        var with = ArchitectureAskPrompts.Plan("o que é isso?", "catálogo", true);
        Assert.Contains("\"attachments\"", with);
        Assert.Contains("COPIADO LITERALMENTE", with);
        Assert.Contains("o anexo É a pergunta", with);
        var without = ArchitectureAskPrompts.Plan("o que é isso?", "catálogo");
        Assert.DoesNotContain("attachments", without);
        Assert.DoesNotContain("anexou", without);
    }

    [Fact]
    public void Answer_prompt_carries_the_attachment_context_and_the_rule_to_use_it()
    {
        var sources = new[] { new AskSource("RN-1 — x", "texto") };
        var with = ArchitectureAskPrompts.Answer("o que é isso?", ArchitectureQuestionKind.Operation, sources, 6, "O que a IA viu nos anexos: erro 500");
        Assert.Contains("ANEXOS DO USUÁRIO:", with);
        Assert.Contains("erro 500", with);
        Assert.Contains("o bloco ANEXOS é parte da pergunta", with);
        var without = ArchitectureAskPrompts.Answer("o que é isso?", ArchitectureQuestionKind.Operation, sources, 6);
        Assert.DoesNotContain("ANEXOS", without);
    }

    [Fact]
    public void Attachment_context_has_what_the_ai_saw_the_head_and_the_tail_of_text_and_lists_images()
    {
        var log = "INICIO\n" + new string('x', 10_000) + "\nERRO FINAL: timeout";
        var atts = AskAttachmentRules.Validate([In("print.png", null, Png()), Text("app.log", log)]).Attachments;
        var context = ArchitectureAskPrompts.AttachmentContext("tela de usuários, erro 500", atts)!;
        Assert.Contains("tela de usuários, erro 500", context);
        Assert.Contains("Imagem \"print.png\"", context);
        Assert.Contains("INICIO", context);
        Assert.Contains("ERRO FINAL: timeout", context);
        Assert.Contains("meio omitido", context);
        Assert.True(context.Length < 5_000);
        Assert.Null(ArchitectureAskPrompts.AttachmentContext("x", []));
    }
}
