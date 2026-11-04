using System.Text.Json;
using solvace.azure.domain.Models;
using solvace.executionplans.domain.Entities;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0076: evidências do card no DevOps — o que é achado, em que ordem, e o que pode ser baixado.</summary>
public class CardEvidenceTests
{
    private const string Org = "electradv";
    private const string A1 = "11111111-1111-1111-1111-111111111111";
    private const string A2 = "22222222-2222-2222-2222-222222222222";
    private const string A3 = "33333333-3333-3333-3333-333333333333";
    private const string A4 = "44444444-4444-4444-4444-444444444444";

    private static string Url(string id, string file) => $"https://dev.azure.com/{Org}/0f2c9a7e-aaaa-bbbb-cccc-1234567890ab/_apis/wit/attachments/{id}?fileName={file}";

    private static JsonElement WorkItem() => JsonDocument.Parse($$"""
        {
          "id": 77698,
          "fields": {
            "System.Title": "Calendário Invalid date",
            "System.Description": "<div>ver também <a href=\"{{Url(A2, "planilha.xlsx")}}\">planilha</a></div>",
            "Microsoft.VSTS.TCM.ReproSteps": "<div>1. abrir o filtro</div><div><img src=\"{{Url(A1, "image.png")}}\" alt=Image></div><div>2. voltar</div>",
            "System.History": "<img src=\"{{Url(A4, "historico.png")}}\">",
            "Custom.Evidencias": "<img src=\"{{Url(A1, "image.png")}}\">"
          },
          "relations": [
            { "rel": "AttachedFile", "url": "https://dev.azure.com/{{Org}}/0f2c/_apis/wit/attachments/{{A3}}",
              "attributes": { "name": "log_erro.txt", "resourceSize": 2048, "resourceCreatedDate": "2026-11-03T13:00:00Z", "comment": "log do servidor" } },
            { "rel": "System.LinkTypes.Hierarchy-Reverse", "url": "https://dev.azure.com/{{Org}}/_apis/wit/workItems/1" }
          ]
        }
        """).RootElement;

    [Fact]
    public void Finds_field_images_then_discussion_then_attachments_without_repeating()
    {
        var comments = new[]
        {
            new AzureCardComment { Text = $"<p>segue o print</p><img src=\"{Url(A4, "print%20cliente.png")}\">", CreatedByName = "Fulano", CreatedDate = DateTimeOffset.Parse("2026-11-03T15:00:00Z") },
            new AzureCardComment { Text = $"<img src=\"{Url(A1, "image.png")}\">", CreatedByName = "Beltrano", CreatedDate = DateTimeOffset.Parse("2026-11-03T16:00:00Z") },
        };

        var list = AzureCardEvidenceParser.Parse(WorkItem(), comments);

        Assert.Equal([A1, A2, A4, A3], list.Select(e => e.Key));
        Assert.Equal(("campo", "Repro Steps", "image.png"), (list[0].Source, list[0].SourceDetail, list[0].FileName));
        Assert.Equal(("campo", "Descrição", "planilha.xlsx"), (list[1].Source, list[1].SourceDetail, list[1].FileName));
        Assert.Equal(("discussion", "Fulano", "print cliente.png"), (list[2].Source, list[2].SourceDetail, list[2].FileName));
        Assert.Equal(("anexo", "log_erro.txt", 2048L, "log do servidor"), (list[3].Source, list[3].FileName, list[3].Size!.Value, list[3].Comment));
    }

    [Fact]
    public void History_field_is_not_a_source_but_the_comment_is()
    {
        var list = AzureCardEvidenceParser.Parse(WorkItem(), []);
        Assert.DoesNotContain(list, e => e.Key == A4);
    }

    [Theory]
    [InlineData("https://dev.azure.com/electradv/proj/_apis/wit/attachments/" + A1 + "?fileName=a.png", true)]
    [InlineData("https://electradv.visualstudio.com/proj/_apis/wit/attachments/" + A1, true)]
    [InlineData("https://dev.azure.com/outraorg/proj/_apis/wit/attachments/" + A1, false)]
    [InlineData("http://dev.azure.com/electradv/proj/_apis/wit/attachments/" + A1, false)]
    [InlineData("https://evil.example.com/electradv/_apis/wit/attachments/" + A1, false)]
    [InlineData("https://dev.azure.com/electradv/proj/_apis/wit/workitems/1", false)]
    public void Downloads_only_attachments_of_the_configured_organization(string url, bool allowed) =>
        Assert.Equal(allowed, AzureCardEvidenceParser.IsAllowedDownload(url, Org));

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("print <cliente>.png", "print _cliente_.png")]
    [InlineData("", "arquivo")]
    public void File_names_are_safe(string given, string expected) => Assert.Equal(expected, AzureCardEvidenceParser.SafeName(given));

    [Theory]
    [InlineData("ev01-descricao-image.png", "image/png", true)]
    [InlineData("ev02-anexo-planilha.xlsx", "application/octet-stream", true)]
    [InlineData("ev03-anexo-relatorio.pdf", "application/pdf", true)]
    [InlineData("ev04-anexo-log", "text/plain", true)]
    [InlineData("ev05-anexo-gravacao.mp4", "video/mp4", false)]
    [InlineData("ev06-anexo-dump.zip", "application/zip", false)]
    public void Readable_formats(string name, string type, bool readable) => Assert.Equal(readable, EvidenceFormats.IsReadable(name, type));
}
