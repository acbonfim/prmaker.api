using solvace.executionplans.application;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>0099 ajustes 1: filtros do board salvos no épico (jsonb) e as tags usadas nos épicos.</summary>
public class EpicBoardFilters0099Tests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-24T12:00:00Z");

    private static Epic New() => new("Bugs do 5S", null, null, ["Lindt"], "Time Revamp", "Stories", null, Guid.NewGuid(), "Alex", Now);

    [Fact]
    public void Epico_novo_tem_filtros_vazios_sempre_objeto()
    {
        var f = New().BoardFilters;
        Assert.False(f.HasAny());
        Assert.Empty(f.Lanes);
        Assert.Equal(string.Empty, f.Text);
        Assert.NotNull(EpicSummary.From(New(), null).BoardFilters);
    }

    [Fact]
    public void Grava_filtros_limpos_sem_mexer_na_data_de_atualizacao()
    {
        var epic = New();
        var changed = epic.SetBoardFilters(new EpicBoardFilters
        {
            Lanes = ["(padrão)", "Expedite", "expedite", ""], HiddenColumns = [" Closed ", "closed", ""], Types = [" Bug ", "bug"],
            AssignedTo = ["alex@solvace.com"], Tags = ["Lindt"], Text = "  exportação  "
        });
        Assert.True(changed);
        Assert.Equal(Now, epic.UpdatedAt);
        var f = epic.BoardFilters;
        Assert.Equal(["", "Expedite"], f.Lanes);
        Assert.Equal(["Bug"], f.Types);
        Assert.Equal(["Closed"], f.HiddenColumns);
        Assert.Equal("exportação", f.Text);
        Assert.Contains("\"lanes\":[\"\",\"Expedite\"]", epic.BoardFiltersJson);

        // Mesmo conteúdo (outra grafia/ordem de espaços) não é mudança.
        Assert.False(epic.SetBoardFilters(new EpicBoardFilters
        {
            Lanes = ["", "Expedite"], HiddenColumns = ["Closed"], Types = ["Bug"], AssignedTo = ["alex@solvace.com"], Tags = ["Lindt"],
            Text = "exportação"
        }));
        Assert.True(epic.SetBoardFilters(new EpicBoardFilters { HiddenColumns = ["Closed"] }));
        Assert.True(epic.BoardFilters.HasAny());
        // {} limpa.
        Assert.True(epic.SetBoardFilters(new EpicBoardFilters()));
        Assert.False(epic.BoardFilters.HasAny());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"lanes": null, "text": null}""")]
    [InlineData("""{"types": ["Bug"]}""")]
    [InlineData("not json")]
    public void Linha_antiga_ou_parcial_vira_listas_vazias(string json)
    {
        var epic = New();
        typeof(Epic).GetProperty(nameof(Epic.BoardFiltersJson))!.SetValue(epic, json);
        var f = epic.BoardFilters;
        Assert.NotNull(f.Lanes);
        Assert.NotNull(f.AssignedTo);
        Assert.NotNull(f.Tags);
        Assert.NotNull(f.Text);
    }

    [Fact]
    public void Filtro_com_valores_demais_recusa()
    {
        var many = Enumerable.Range(0, EpicBoardFilters.MaxValues + 1).Select(i => $"t{i}").ToList();
        Assert.Throws<DomainException>(() => New().SetBoardFilters(new EpicBoardFilters { Tags = many }));
    }

    [Fact]
    public void Tags_dos_epicos_mais_usadas_primeiro_depois_nome()
    {
        var tags = EpicApplication.CountTags(
        [
            """["Lindt","Kaizen"]""", """["lindt","5S"]""", """["Lindt"]""", """["Ação"]""", "[]", null, "lixo"
        ], null);
        Assert.Equal(["Lindt", "5S", "Ação", "Kaizen"], tags);
        Assert.Equal(["Ação"], EpicApplication.CountTags(["""["Ação","Kaizen"]"""], "acao"));
        Assert.Equal(["Lindt"], EpicApplication.CountTags(["""["Lindt","Kaizen"]"""], "IND"));
    }

    [Fact]
    public void Filtro_de_sla_0105_grava_e_linha_antiga_sem_a_chave_vem_vazia()
    {
        var epic = New();
        Assert.True(epic.SetBoardFilters(new EpicBoardFilters { Sla = ["Grave", " attention ", "grave", ""] }));
        Assert.Equal(["grave", "attention"], epic.BoardFilters.Sla);
        Assert.True(epic.BoardFilters.HasAny());
        // JSON gravado antes da 0105 (sem "sla")
        var old = System.Text.Json.JsonSerializer.Deserialize<EpicBoardFilters>("""{"types":["Bug"],"text":""}""")!;
        Assert.NotNull(old.Sla);
        Assert.Empty(old.Sla);
    }
}
