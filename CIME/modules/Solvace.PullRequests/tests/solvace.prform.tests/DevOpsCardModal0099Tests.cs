using System.Text.Json;
using solvace.azure.application.Services;
using solvace.azure.domain.Models;
using solvace.prform.Execution;
using Xunit;

namespace solvace.prform.tests;

/// <summary>
/// 0099 (ajustes 3, modal do card): menções do DevOps no comentário (<c>#123</c> e <c>@Nome</c>), discussion (parse dos comentários)
/// e anexos (leitor das evidências + imagem pelo nome).
/// </summary>
public class DevOpsCardModal0099Tests
{
    private static string Url(int id) => $"https://dev.azure.com/org/Proj/_workitems/edit/{id}";
    private const string Guid1 = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void Card_mention_vira_link_do_devops()
    {
        var html = DevOpsDeskMapping.LinkMentions("<p>Veja o #78123 e (#5).</p>", Url);
        Assert.Equal("<p>Veja o <a href=\"https://dev.azure.com/org/Proj/_workitems/edit/78123\" data-vss-mention=\"version:1.0\">#78123</a> e " +
                     "(<a href=\"https://dev.azure.com/org/Proj/_workitems/edit/5\" data-vss-mention=\"version:1.0\">#5</a>).</p>", html);
    }

    [Theory]
    [InlineData("<p>it&#39;s ok</p>")]
    [InlineData("<p><a href=\"x\">#123</a></p>")]
    [InlineData("<p><code>#123</code></p>")]
    [InlineData("<pre><code>x #123\n</code></pre>")]
    [InlineData("<p>abc#123 /#45 #12345678</p>")]
    [InlineData("<h1 id=\"x\">Titulo</h1>")]
    public void Card_mention_nao_mexe_em_entidade_link_codigo_e_atributo(string html) =>
        Assert.Equal(html, DevOpsDeskMapping.LinkMentions(html, Url));

    [Fact]
    public void Card_mention_volta_ao_texto_depois_do_link()
    {
        var html = DevOpsDeskMapping.LinkMentions("<p><a href=\"x\">#1</a> e #2</p>", Url);
        Assert.Contains("<a href=\"x\">#1</a> e <a href=\"https://dev.azure.com/org/Proj/_workitems/edit/2\"", html);
    }

    [Fact]
    public void Pessoa_com_guid_vira_mencao_e_nome_mais_longo_ganha()
    {
        var people = new List<DevOpsDeskMapping.MentionTarget>
        {
            new("Alex", "AAAAAAAA-0000-0000-0000-000000000000"),
            new("Alex Carlos", Guid1),
        };
        var html = DevOpsDeskMapping.LinkMentions("<p>@alex carlos pode ver? cc @Alex</p>", Url, people);
        Assert.Equal($"<p><a href=\"#\" data-vss-mention=\"version:2.0,{Guid1}\">@alex carlos</a> pode ver? cc " +
                     "<a href=\"#\" data-vss-mention=\"version:2.0,aaaaaaaa-0000-0000-0000-000000000000\">@Alex</a></p>", html);
    }

    [Fact]
    public void Pessoa_sem_guid_fica_texto_e_nome_colado_nao_casa()
    {
        var people = new List<DevOpsDeskMapping.MentionTarget> { new("Maria", "descriptor-aad.xyz"), new("Ana", Guid1) };
        Assert.Equal("<p>@Maria e @Anabela</p>", DevOpsDeskMapping.LinkMentions("<p>@Maria e @Anabela</p>", Url, people));
    }

    [Fact]
    public void Pessoa_com_acento_e_ecomercial_no_nome()
    {
        var people = new List<DevOpsDeskMapping.MentionTarget> { new("João & Cia", Guid1) };
        var html = DevOpsDeskMapping.LinkMentions("<p>oi @João &amp; Cia, ver #7</p>", Url, people);
        Assert.Contains($"data-vss-mention=\"version:2.0,{Guid1}\">@João &amp; Cia</a>", html);
        Assert.Contains("data-vss-mention=\"version:1.0\">#7</a>", html);
    }

    [Fact]
    public void Comentario_markdown_completo_vira_html_com_mencoes()
    {
        var html = DevOpsDeskMapping.LinkMentions(UserStorySpec.ToHtml("Olá @Ana, o **#42** tem `#43`"), Url,
            [new DevOpsDeskMapping.MentionTarget("Ana", Guid1)]);
        Assert.Contains($"version:2.0,{Guid1}\">@Ana</a>", html);
        Assert.Contains("edit/42\" data-vss-mention=\"version:1.0\">#42</a>", html);
        Assert.Contains("<code>#43</code>", html);
    }

    private const string CommentsJson = """
    {
      "totalCount": 3,
      "count": 3,
      "comments": [
        { "id": 10, "text": "<div>primeiro</div>", "createdBy": { "displayName": "Nissy Mbambu", "uniqueName": "nissy@x.com", "id": "g1",
          "_links": { "avatar": { "href": "https://img/1" } } }, "createdDate": "2026-11-20T10:00:00Z", "modifiedDate": "2026-11-20T10:00:00.5Z" },
        { "id": 11, "text": "apagado", "isDeleted": true, "createdDate": "2026-11-21T10:00:00Z" },
        { "id": 12, "format": "markdown", "text": "**md**", "renderedText": "<p><strong>md</strong></p>",
          "createdBy": { "displayName": "Alex Carlos", "uniqueName": "alex@x.com", "id": "g2", "imageUrl": "https://img/2" },
          "createdDate": "2026-11-22T10:00:00Z", "modifiedDate": "2026-11-22T11:00:00Z" }
      ]
    }
    """;

    [Fact]
    public void Comentarios_mais_novos_primeiro_sem_excluidos_e_markdown_renderizado()
    {
        using var doc = JsonDocument.Parse(CommentsJson);
        var result = DevOpsDeskMapping.ParseComments(doc.RootElement);
        Assert.Equal(3, result.Count);
        Assert.Equal([12, 10], result.Comments.Select(c => c.Id));
        var md = result.Comments[0];
        Assert.Equal("<p><strong>md</strong></p>", md.Html);
        Assert.True(md.Edited);
        Assert.Equal("Alex Carlos", md.Author?.DisplayName);
        Assert.Equal("https://img/2", md.Author?.ImageUrl);
        var first = result.Comments[1];
        Assert.False(first.Edited);
        Assert.Equal("https://img/1", first.Author?.ImageUrl);
        Assert.Equal("nissy@x.com", first.Author?.UniqueName);
    }

    [Fact]
    public void Anexos_do_card_campos_e_discussion_com_imagem_pelo_nome()
    {
        const string workItem = """
        {
          "id": 78794,
          "fields": {
            "Microsoft.VSTS.TCM.ReproSteps": "<div><img src=\"https://dev.azure.com/org/abc/_apis/wit/attachments/aaaaaaaa-1111-2222-3333-444444444444?fileName=image.png\"></div>"
          },
          "relations": [
            { "rel": "AttachedFile", "url": "https://dev.azure.com/org/abc/_apis/wit/attachments/BBBBBBBB-1111-2222-3333-444444444444",
              "attributes": { "name": "log.txt", "resourceSize": 2048, "resourceCreatedDate": "2026-11-20T10:00:00Z", "comment": "log do erro" } },
            { "rel": "System.LinkTypes.Related", "url": "https://dev.azure.com/org/abc/_apis/wit/workItems/1" }
          ]
        }
        """;
        using var doc = JsonDocument.Parse(workItem);
        var comments = DevOpsDeskMapping.EvidenceComments([
            new DevOpsComment(1, "<img src=\"https://dev.azure.com/org/abc/_apis/wit/attachments/cccccccc-1111-2222-3333-444444444444?fileName=tela.JPG\">",
                new DevOpsPersonInfo("Ana", "ana@x.com", "g", null), DateTimeOffset.Parse("2026-11-21T10:00:00Z"), null, false)
        ]);
        var list = DevOpsDeskMapping.ParseAttachments(doc.RootElement, comments);
        Assert.Equal(3, list.Count);
        var field = list.Single(a => a.Source == "campo");
        Assert.Equal("aaaaaaaa-1111-2222-3333-444444444444", field.Id);
        Assert.Equal("Repro Steps", field.SourceDetail);
        Assert.True(field.IsImage);
        var discussion = list.Single(a => a.Source == "discussion");
        Assert.Equal("Ana", discussion.SourceDetail);
        Assert.True(discussion.IsImage);
        var file = list.Single(a => a.Source == "anexo");
        Assert.Equal("bbbbbbbb-1111-2222-3333-444444444444", file.Id);
        Assert.Equal("log.txt", file.Name);
        Assert.Equal(2048, file.Size);
        Assert.Equal("log do erro", file.Comment);
        Assert.False(file.IsImage);
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.JPEG", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.svg", false)]
    [InlineData("a.pdf", false)]
    [InlineData(null, false)]
    public void Imagem_pelo_nome(string? name, bool image) => Assert.Equal(image, DevOpsDeskMapping.IsImageName(name));

    [Fact]
    public void Content_type_pelo_nome() =>
        Assert.Equal(["image/png", "application/pdf", "application/octet-stream"],
            new[] { "x.PNG", "y.pdf", "z.bin" }.Select(DevOpsDeskMapping.ContentTypeFromName));

    [Fact]
    public void Referencia_do_anexo_no_mcp()
    {
        var list = new List<DevOpsAttachmentInfo>
        {
            new("a", "image.png", "u", "campo", null, null, null, null, true),
            new("b", "log-erro.txt", "u", "anexo", null, null, null, null, false),
        };
        Assert.Equal("b", DevOpsMcpTools.ResolveAttachment(list, "#2")?.Id);
        Assert.Equal("a", DevOpsMcpTools.ResolveAttachment(list, "1")?.Id);
        Assert.Equal("b", DevOpsMcpTools.ResolveAttachment(list, "erro")?.Id);
        Assert.Null(DevOpsMcpTools.ResolveAttachment(list, "#3"));
    }

    [Fact]
    public void Html_do_comentario_vira_texto_curto() =>
        Assert.Equal("Linha 1\nLinha 2 [imagem] & fim", DevOpsDeskMapping.HtmlToText("<div>Linha 1</div><div>Linha 2 <img src=\"x\"> &amp; fim</div>"));
}
