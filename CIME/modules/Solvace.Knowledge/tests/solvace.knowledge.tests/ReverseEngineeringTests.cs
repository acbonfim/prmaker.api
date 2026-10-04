using solvace.knowledge.domain.Entities;
using solvace.knowledge.domain.Reverse;
using Xunit;

namespace solvace.knowledge.tests;

public class ReverseEngineeringTests
{
    private const string Doc = """
        # Levantamento funcional — Kaizen

        ## Resumo do módulo
        Ideias de melhoria.

        ## Perfis e permissões
        ### PRF-001 — Aprovador da etapa
        - **Onde:** `KaizenController.cs:40`

        ## Funcionalidades
        ### FN-1 — Cadastrar ideia
        Usa TELA-001 e RN-012.

        ## Casos de uso
        ### UC-001 — Colaborador registra uma ideia
        - **Onde:** `KaizenService.cs:88`, `kaizen-form.component.ts:120`
        1. Abre a TELA-001 e chama revamp-users#API-004.

        ## Regras de negócio
        #### Etapas
        ### RN-012 — Etapa só avança com aprovador
        - **Onde:** `KaizenService.cs:210-230`
        - **Tabelas:** TB_MLH_MELHORIA, `TB_MLH_ETAPA`
        - **Módulos:** revamp-users, Legado-Kaizen
        - **Tags:** etapa, aprovação, workflow
        Mensagem: "Informe o aprovador".
        ```sql
        ## isto não é cabeçalho
        SELECT * FROM TB_MLH_LOG
        ```
        ### RN-013 — (removido) virou configuração
        ## Estados e ciclo de vida
        ## Notificações
        ## Configurações e parâmetros
        ## Relatórios e indicadores
        ## Integrações com outros módulos
        ## Lacunas e pontos a confirmar
        """;

    [Fact]
    public void Parses_items_with_metadata_and_ignores_code_fences()
    {
        var items = ReverseDocParser.Parse(Doc);
        Assert.Equal(["PRF-001", "FN-001", "UC-001", "RN-012", "RN-013"], items.Select(i => i.Id));

        var rn = items.Single(i => i.Id == "RN-012");
        Assert.Equal("RN", rn.Kind);
        Assert.Equal("Etapa só avança com aprovador", rn.Title);
        Assert.Contains("TB_MLH_MELHORIA", rn.Tables);
        Assert.Contains("TB_MLH_ETAPA", rn.Tables);
        Assert.Contains("TB_MLH_LOG", rn.Tables);
        Assert.Equal(["revamp-users", "legado-kaizen"], rn.Modules);
        Assert.Equal(["etapa", "aprovação", "workflow"], rn.Tags);
        Assert.Contains("KaizenService.cs:210-230", rn.Evidence);
        Assert.Contains("## isto não é cabeçalho", rn.Body);
        Assert.DoesNotContain("RN-013", rn.Body);

        var uc = items.Single(i => i.Id == "UC-001");
        Assert.Contains("TELA-001", uc.Refs);
        Assert.Contains("revamp-users#API-004", uc.Refs);
        Assert.Equal(2, uc.Evidence.Count);
        Assert.True(items.Single(i => i.Id == "RN-013").Removed);
    }

    [Fact]
    public void Lint_reports_structure_evidence_removed_and_unknown_refs()
    {
        var type = ReverseDocTypes.Get("funcional");
        var lint = ReverseLint.Run(type, Doc, publishedIds: ["RN-012", "RN-020"], otherDocIds: new Dictionary<string, string> { ["TELA-001"] = "uiux" },
            coverage: 0.5, minCoverage: 0.9);
        Assert.True(lint.Ok, string.Join(" | ", lint.Errors));
        Assert.Equal(4, lint.Items);
        Assert.Equal(["RN-020"], lint.RemovedIds);
        Assert.DoesNotContain("RN-012", lint.WithoutEvidence);
        Assert.Empty(lint.WithoutEvidence);
        Assert.Contains(lint.Warnings, w => w.Contains("RN-020"));
        Assert.Contains(lint.Warnings, w => w.Contains("Cobertura"));
        Assert.Empty(lint.UnknownRefs);
    }

    [Fact]
    public void Lint_blocks_missing_headings_duplicates_clashes_and_secrets()
    {
        var type = ReverseDocTypes.Get("funcional");
        var doc = """
            ## Regras de negócio
            ### RN-001 — A
            ### RN-001 — B
            ### UC-002 — C
            connection: Server=db;User Id=x;Password=segredo123
            """;
        var lint = ReverseLint.Run(type, doc, otherDocIds: new Dictionary<string, string> { ["UC-002"] = "arquitetura" });
        Assert.False(lint.Ok);
        Assert.Contains(lint.Errors, e => e.Contains("Faltam seções"));
        Assert.Contains(lint.Errors, e => e.Contains("RN-001"));
        Assert.Contains(lint.Errors, e => e.Contains("UC-002"));
        Assert.Contains(lint.Errors, e => e.Contains("segredo"));
        Assert.Contains("RN-001", lint.WithoutEvidence);
    }

    [Fact]
    public void Lint_does_not_flag_business_text_about_passwords()
    {
        var lint = ReverseLint.Run(ReverseDocTypes.Get("funcional"), "## Regras de negócio\n### RN-001 — Senha: mínimo 8 caracteres\n- **Onde:** `a.cs:1`\nA chave `Email:Password` é lida do Secret Manager.");
        Assert.DoesNotContain(lint.Errors, e => e.Contains("segredo"));
    }

    [Fact]
    public void Revision_follows_the_review_cycle()
    {
        var now = DateTimeOffset.UtcNow;
        var r = new ReverseRevision("revamp-kaizen", "funcional", 1, "new", null, null, "dev", now);
        Assert.Throws<DomainException>(() => r.Submit("dev", now));
        r.Save("## x", "primeira versão", null, 0.95, null, byApprover: false, "dev", now);
        r.Submit("dev", now);
        Assert.Equal(ReverseRevisionStatus.Review, r.Status);
        Assert.Throws<DomainException>(() => r.MarkPublished(1, "gestor", now));
        Assert.Throws<DomainException>(() => r.Review("changes", " ", "gestor", now));
        r.Review("changes", "faltam as regras de etapa", "gestor", now);
        Assert.Equal(ReverseRevisionStatus.Changes, r.Status);
        r.Save("## y", null, null, null, null, byApprover: false, "dev", now);
        r.Submit("dev", now);
        r.Review("approve", null, "gestor", now);
        r.Save("## y editado pelo revisor", null, null, null, null, byApprover: true, "gestor", now);
        Assert.Equal(ReverseRevisionStatus.Approved, r.Status);
        r.MarkPublished(3, "gestor", now);
        Assert.Equal(ReverseRevisionStatus.Published, r.Status);
        Assert.Equal(3, r.PublishedVersion);
        Assert.Throws<DomainException>(() => r.Save("## z", null, null, null, null, true, "gestor", now));
        r.Supersede(now);
        Assert.Equal(ReverseRevisionStatus.Superseded, r.Status);
    }

    [Fact]
    public void Author_editing_a_submitted_revision_sends_it_back_to_draft()
    {
        var now = DateTimeOffset.UtcNow;
        var r = new ReverseRevision("revamp-kaizen", "arquitetura", 2, "improve", "## a", 4, "dev", now);
        r.Submit("dev", now);
        r.Save("## b", null, null, null, null, byApprover: false, "dev", now);
        Assert.Equal(ReverseRevisionStatus.Draft, r.Status);
    }

    [Theory]
    [InlineData("RN-12", null, "RN-012")]
    [InlineData("revamp-kaizen#rn-012", "revamp-kaizen", "RN-012")]
    [InlineData("revamp-kaizen RN-7", "revamp-kaizen", "RN-007")]
    [InlineData("TELA-0003", null, "TELA-003")]
    public void Parses_references(string value, string? module, string id)
    {
        var parsed = ReverseItemKinds.ParseRef(value);
        Assert.NotNull(parsed);
        Assert.Equal(module, parsed!.Value.Module);
        Assert.Equal(id, parsed.Value.Id);
    }

    [Fact]
    public void Rejects_non_item_references_and_bad_assets()
    {
        Assert.Null(ReverseItemKinds.ParseRef("ART-21"));
        Assert.Null(ReverseItemKinds.ParseRef("revamp-kaizen/020-modulos"));
        Assert.Throws<DomainException>(() => ReverseAsset.Link("revamp-kaizen", null, "Figma", "javascript:alert(1)", null, null, "x", DateTimeOffset.UtcNow));
        var figma = ReverseAsset.Link("revamp-kaizen", "file", "Figma", "https://www.figma.com/file/abc", null, ["tela-001"], "x", DateTimeOffset.UtcNow);
        Assert.Equal("figma", figma.Kind);
        Assert.Equal(["TELA-001"], figma.Screens);
    }

    [Fact]
    public void Every_doc_type_template_passes_its_own_heading_check()
    {
        foreach (var type in ReverseDocTypes.All)
        {
            var headings = ReverseDocParser.Headings(type.Template).Where(h => h.Level == 2).Select(h => ReverseLint.Normalize(h.Text)).ToList();
            foreach (var required in type.Headings)
                Assert.True(headings.Any(h => h.Contains(required.Match)), $"{type.Key}: o modelo não tem '{required.Title}'");
            Assert.All(type.Kinds, k => Assert.True(ReverseItemKinds.IsKind(k), k));
        }
    }
}
