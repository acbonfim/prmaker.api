using solvace.prform.domain.Promotion;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0074: leitura da mensagem do Release Manager — cards por regex, versão, regras e validação da resposta da IA.</summary>
public class PromotionMessageParserTests
{
    private const string Message = """
        Boa tarde pessoal, tudo e Todos bem?

        Estou iniciando os preparativos para o hotfix V3.274.10

        Podem enviar o pr do ticket para o hotfix?

        Abaixo seguem as instruções de branches:


        Branch para ticket Netcore: hotfix-version
        Branch para tickets com API De Integrações: release-version e master
        Branch para tickets Angular: release-version, edge, master (draft)


        ID    Work Item Type    Title    Assigned To
        77317    Bug    Gerdau - Araçariguama - Documentation - Botão "mostrar conteúdo" está sendo sobreposto pelo "Tipo"
        Alex Carlos

        76607    Bug    EFESO Sandbox - Checklist - Create New Version not working
        Alex Carlos

        76462    Bug    [HOME] - Barra de LIL e CHK na lista de tarefas está apresentando com % e deveria ser o valor de Questões
        Bruno Ferreira

        76781    User Story    Merge Users - Include e-mail or username in the Users list
        Fábio Moraes

        77349    Bug    Gerdau - Ouro Branco - Checklist - Falha ao responder inspeções nos checklist
        Joseandeson Jesus
        """;

    [Fact]
    public void Cards_are_extracted_with_type_title_and_assignee()
    {
        var cards = PromotionMessageParser.ExtractCards(Message);

        Assert.Equal(["77317", "76607", "76462", "76781", "77349"], cards.Select(c => c.Card).ToArray());
        Assert.Equal("Bug", cards[0].WorkItemType);
        Assert.Equal("User Story", cards[3].WorkItemType);
        Assert.Equal("Alex Carlos", cards[0].AssignedTo);
        Assert.Equal("Fábio Moraes", cards[3].AssignedTo);
        Assert.StartsWith("Gerdau - Araçariguama", cards[0].Title);
        Assert.Contains("[HOME]", cards[2].Title);
    }

    [Fact]
    public void Duplicate_cards_are_ignored_and_empty_text_gives_nothing()
    {
        var text = "1111\tBug\tUm título\tAna\n1111\tBug\tUm título\tAna\n";
        var cards = PromotionMessageParser.ExtractCards(text);
        Assert.Single(cards);
        Assert.Equal("Ana", cards[0].AssignedTo); // colado de planilha: responsável na mesma linha

        Assert.Empty(PromotionMessageParser.ExtractCards(null));
        Assert.Empty(PromotionMessageParser.ExtractCards("só conversa, sem cards"));
    }

    [Fact]
    public void Stripping_the_table_keeps_the_free_text_and_drops_cards_and_assignees()
    {
        var stripped = PromotionMessageParser.StripCardTable(Message);

        Assert.Contains("hotfix V3.274.10", stripped);
        Assert.Contains("Branch para ticket Netcore: hotfix-version", stripped);
        Assert.DoesNotContain("Gerdau", stripped);
        Assert.DoesNotContain("Alex Carlos", stripped);
        Assert.DoesNotContain("Work Item Type", stripped);
    }

    [Theory]
    [InlineData("Estou iniciando os preparativos para o hotfix V3.274.10", "hotfix", "3.274.10")]
    [InlineData("release v3.280.0 amanhã", "release", "3.280.0")]
    [InlineData("vamos subir a V3.1.2 hoje", null, "3.1.2")]
    [InlineData("sem versão nenhuma", null, null)]
    public void Kind_and_version_are_found(string text, string? kind, string? version)
    {
        Assert.Equal((kind, version), PromotionMessageParser.ExtractKindAndVersion(text));
    }

    [Fact]
    public void Rules_are_read_from_the_free_text_without_ai()
    {
        var rules = PromotionMessageParser.ExtractRules(Message);

        Assert.Equal(3, rules.Count);
        Assert.Equal("Netcore", rules[0].TicketType);
        Assert.Equal(["hotfix-{version}"], rules[0].Targets.Select(t => t.Branch).ToArray());

        Assert.Equal("API De Integrações", rules[1].TicketType);
        Assert.Equal(["release-{version}", "master"], rules[1].Targets.Select(t => t.Branch).ToArray());

        Assert.Equal("Angular", rules[2].TicketType);
        Assert.Equal(["release-{version}", "edge", "master"], rules[2].Targets.Select(t => t.Branch).ToArray());
        Assert.False(rules[2].Targets[1].Draft);
        Assert.True(rules[2].Targets[2].Draft);
        Assert.All(rules, r => Assert.Null(r.Kind));
    }

    [Theory]
    [InlineData("release-version", "release-{version}")]
    [InlineData("hotfix/version", "hotfix/{version}")]
    [InlineData("master", "master")]
    [InlineData("edge", "edge")]
    [InlineData("`release-version`", "release-{version}")]
    [InlineData("rm -rf /", null)]
    [InlineData("a..b", null)]
    [InlineData("", null)]
    public void Branch_templates_are_normalized_or_rejected(string token, string? expected)
    {
        Assert.Equal(expected, PromotionMessageParser.NormalizeTemplate(token));
    }

    [Fact]
    public void Ai_json_is_validated_against_the_known_kinds()
    {
        const string json = """
            Claro! Aqui está:
            ```json
            {"version": "V3.274.10", "kind": "HOTFIX", "rules": [
              {"ticketType": "Netcore", "kind": "legacy", "targets": [{"branch": "hotfix-version"}]},
              {"ticketType": "Angular", "kind": "revamp-frontend", "targets": [
                 {"branch": "release-version"}, {"branch": "edge"}, {"branch": "master", "draft": true}, {"branch": "rm -rf /"}]},
              {"ticketType": "Outro", "kind": "inventado", "targets": [{"branch": "x"}]}
            ]}
            ```
            """;

        var (version, kind, rules, warnings) = PromotionMessageParser.ParseAiJson(json, ["legacy", "integration-api", "revamp-frontend"]);

        Assert.Equal("3.274.10", version);
        Assert.Equal("hotfix", kind);
        Assert.Equal(2, rules.Count);
        Assert.Equal("legacy", rules[0].Kind);
        Assert.Equal(["release-{version}", "edge", "master"], rules[1].Targets.Select(t => t.Branch).ToArray());
        Assert.True(rules[1].Targets[2].Draft);
        Assert.Contains(warnings, w => w.Contains("inventado"));
        Assert.Contains(warnings, w => w.Contains("nome inválido"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("não sei")]
    [InlineData("{quebrado")]
    public void Garbage_from_the_ai_becomes_a_warning_not_fields(string? content)
    {
        var (version, kind, rules, warnings) = PromotionMessageParser.ParseAiJson(content, ["legacy"]);
        Assert.Null(version);
        Assert.Null(kind);
        Assert.Empty(rules);
        Assert.Single(warnings);
    }

    private static readonly PromotionProfileSet Profiles = PromotionProfileSet.Parse("""
        {"profiles": {
          "legacy": {"label": "Netcore (legado)", "targets": [{"branch": "hotfix-{version}"}]},
          "integration-api": {"label": "API de Integrações", "targets": [{"branch": "master"}]},
          "revamp-frontend": {"label": "Angular (revamp)", "targets": [{"branch": "edge"}]},
          "revamp-backend": {"label": "Revamp (back)", "targets": [{"branch": "edge"}]}}}
        """);

    [Theory]
    [InlineData("Netcore", "legacy")]
    [InlineData("API De Integrações", "integration-api")]
    [InlineData("tickets com API de Integracoes", "integration-api")]
    [InlineData("Angular", "revamp-frontend")]
    [InlineData("Revamp", null)]          // casa com Angular (revamp) e Revamp (back): ambíguo
    [InlineData("Mobile", null)]
    [InlineData("", null)]
    public void Ticket_label_is_matched_to_a_repository_kind_by_the_configured_labels(string label, string? kind)
    {
        Assert.Equal(kind, PromotionMessageParser.MatchKind(label, Profiles));
    }
}

