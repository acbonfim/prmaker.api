using System.Text.Json;
using solvace.azure.application.Services;
using solvace.azure.domain.Models;
using solvace.executionplans.domain.Entities;
using solvace.prform.Execution;
using Xunit;

namespace solvace.prform.tests;

/// <summary>
/// 0099 ajustes 1 (Mesa de gestão): pessoas de quatro fontes sem repetir (busca sem caixa/acento) e filtros do board salvos no
/// épico aplicados no servidor com a mesma semântica da tela.
/// </summary>
public class Mesa0099Ajustes1Tests
{
    // ── pessoas ─────────────────────────────────────────────────────────────────────────────

    private static DevOpsPersonInfo P(string name, string login, string id = "", string? image = null) => new(name, login, id, image);

    [Fact]
    public void Pessoas_das_fontes_juntas_sem_repetir_pelo_login_sem_caixa()
    {
        var teams = new[] { P("Maria Souza", "maria@solvace.com", "m1") };
        var org = new[] { P("Alex Carlos", "Alex.Carlos@solvace.com", "aad.x", "https://img/alex"), P("MARIA SOUZA (org)", "MARIA@solvace.com") };
        var search = new[] { P("Alex Carlos", "alex.carlos@solvace.com", "guid-a") };
        var recent = new[] { P("João Pedro", "joao@solvace.com"), P("Maria Souza", "maria@solvace.com", "", "https://img/maria") };

        var people = DevOpsDeskMapping.MergePeople(teams, org, search, recent);

        Assert.Equal(["Alex Carlos", "João Pedro", "Maria Souza"], people.Select(p => p.DisplayName));
        // A primeira fonte vence (nome do time), a foto que faltava vem de outra.
        var maria = people.Single(p => p.UniqueName.Equals("maria@solvace.com", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Maria Souza", maria.DisplayName);
        Assert.Equal("m1", maria.Id);
        Assert.Equal("https://img/maria", maria.ImageUrl);
        Assert.Equal("https://img/alex", people[0].ImageUrl);
    }

    [Theory]
    [InlineData("joao", "João Pedro")]
    [InlineData("JOÃO", "João Pedro")]
    [InlineData("conceicao", "Ana Conceição")]
    [InlineData("alex carlos", "Alex Carlos")]
    [InlineData("carlos alex", "Alex Carlos")]
    [InlineData("ana.c@", "Ana Conceição")]
    public void Busca_de_pessoas_sem_caixa_nem_acento_no_nome_ou_login(string q, string expected)
    {
        var people = DevOpsDeskMapping.MergePeople(new[]
        {
            P("João Pedro", "joao@solvace.com"), P("Ana Conceição", "ana.c@solvace.com"), P("Alex Carlos", "alex.carlos@solvace.com")
        });
        Assert.Equal([expected], DevOpsDeskMapping.FilterPeople(people, q, null).Select(p => p.DisplayName));
    }

    [Fact]
    public void Top_padrao_50_com_busca_e_todos_sem_busca()
    {
        var people = Enumerable.Range(1, 120).Select(i => P($"Pessoa {i:000}", $"p{i}@x.com")).ToList();
        Assert.Equal(50, DevOpsDeskMapping.FilterPeople(people, "pessoa", null).Count);
        Assert.Equal(120, DevOpsDeskMapping.FilterPeople(people, null, null).Count);
        Assert.Equal(10, DevOpsDeskMapping.FilterPeople(people, "pessoa", 10).Count);
        Assert.Equal(5, DevOpsDeskMapping.FilterPeople(people, "", 5).Count);
    }

    [Fact]
    public void Usuarios_da_organizacao_pelo_Graph()
    {
        using var doc = JsonDocument.Parse("""
            { "count": 3, "value": [
              { "subjectKind": "user", "displayName": "Alex Carlos", "principalName": "alex.carlos@solvace.com", "mailAddress": "alex.carlos@solvace.com",
                "descriptor": "aad.AAA", "_links": { "avatar": { "href": "https://vssps/avatar/aad.AAA" } } },
              { "subjectKind": "user", "displayName": "Convidado", "principalName": null, "mailAddress": "guest@cliente.com", "descriptor": "msa.BBB" },
              { "subjectKind": "group", "displayName": "Contributors", "principalName": "[Proj]\\Contributors", "descriptor": "vssgp.CCC" }
            ] }
            """);
        var users = DevOpsDeskMapping.ParseGraphUsers(doc.RootElement);
        Assert.Equal(2, users.Count);
        Assert.Equal(new DevOpsPersonInfo("Alex Carlos", "alex.carlos@solvace.com", "aad.AAA", "https://vssps/avatar/aad.AAA"), users[0]);
        Assert.Equal("guest@cliente.com", users[1].UniqueName);
    }

    [Fact]
    public void Busca_de_identidades_sem_grupos_nem_inativos()
    {
        using var doc = JsonDocument.Parse("""
            { "count": 4, "value": [
              { "id": "11111111-1111-1111-1111-111111111111", "providerDisplayName": "Alex Carlos", "customDisplayName": null, "isActive": true,
                "isContainer": false, "properties": { "Account": { "$type": "System.String", "$value": "alex.carlos@solvace.com" },
                "SchemaClassName": { "$value": "User" } } },
              { "id": "2", "providerDisplayName": "Time Revamp", "isActive": true, "isContainer": true, "properties": {} },
              { "id": "3", "providerDisplayName": "Saiu da empresa", "isActive": false, "isContainer": false,
                "properties": { "Account": { "$value": "saiu@solvace.com" } } },
              { "id": "4", "providerDisplayName": "x", "customDisplayName": "Nome Próprio", "isContainer": false,
                "properties": { "Mail": { "$value": "proprio@solvace.com" } } }
            ] }
            """);
        var found = DevOpsDeskMapping.ParseIdentities(doc.RootElement, "https://dev.azure.com/org");
        Assert.Equal(["Alex Carlos", "Nome Próprio"], found.Select(p => p.DisplayName));
        Assert.Equal("alex.carlos@solvace.com", found[0].UniqueName);
        Assert.Equal("https://dev.azure.com/org/_api/_common/identityImage?id=11111111-1111-1111-1111-111111111111", found[0].ImageUrl);
        Assert.Equal("proprio@solvace.com", found[1].UniqueName);
    }

    [Fact]
    public void Pessoas_dos_cards_recentes_sem_contas_de_servico()
    {
        using var doc = JsonDocument.Parse("""
            [
              { "id": 1, "fields": {
                "System.AssignedTo": { "displayName": "Alex Carlos", "uniqueName": "alex.carlos@solvace.com", "id": "a", "imageUrl": "https://img/a" },
                "System.CreatedBy": { "displayName": "Maria Souza", "uniqueName": "maria@solvace.com", "id": "m" },
                "System.ChangedBy": { "displayName": "Project Collection Build Service (solvace)", "uniqueName": "Build\\0f0e", "id": "b" } } },
              { "id": 2, "fields": {
                "System.CreatedBy": { "displayName": "Microsoft.VisualStudio.Services.TFS", "uniqueName": "00000002-0000-8888-8000-000000000000@2c895908-04e0-4952-89fd-54b0046d6288", "id": "s" },
                "System.ChangedBy": { "displayName": "Alex Carlos", "uniqueName": "ALEX.CARLOS@solvace.com", "id": "a" } } }
            ]
            """);
        var people = DevOpsDeskMapping.MergePeople(DevOpsDeskMapping.PeopleFromWorkItems(doc.RootElement.EnumerateArray()));
        Assert.Equal(["Alex Carlos", "Maria Souza"], people.Select(p => p.DisplayName));
    }

    // ── filtros salvos do épico ─────────────────────────────────────────────────────────────

    private static DevOpsBoardConfig Config() => new()
    {
        Team = "Revamp",
        Board = "Stories",
        Columns =
        [
            new DevOpsBoardColumn { Id = "c-new", Name = "New", ColumnType = "incoming" },
            new DevOpsBoardColumn { Id = "c-dev", Name = "Development" },
            new DevOpsBoardColumn { Id = "c-closed", Name = "Closed", ColumnType = "outgoing" }
        ],
        Lanes = [new DevOpsBoardLane("00000000-0000-0000-0000-000000000000", string.Empty, true), new DevOpsBoardLane("r-exp", "Expedite", false)]
    };

    private static DevOpsBoardItem Item(int id, string type, string title, string state, string? lane = "", string? who = null, params string[] tags) => new()
    {
        Id = id, Type = type, Title = title, State = state, Lane = lane, Tags = [.. tags],
        Column = state switch { "New" => "New", "Resolved" => "Closed", _ => "Development" },
        AssignedTo = who is null ? null : new DevOpsIdentityRef(who.Split('@')[0], who, null)
    };

    private static readonly DevOpsBoardItem[] Items =
    [
        Item(101, "Bug", "Exportação do 5S vazia", "Active", "", "alex@solvace.com", "Lindt", "5S"),
        Item(102, "User Story", "Plano de ação no Kaizen", "New", "Expedite", "maria@solvace.com", "Kaizen"),
        Item(103, "Bug", "Erro ao salvar plano", "Resolved", "Lane que sumiu", null, "Lindt"),
        Item(104, "Task", "Ajustar relatório", "Active", "expedite", "alex@solvace.com", "5S", "Lindt")
    ];

    private static int[] Apply(DevOpsBoardFilters f) => DevOpsDeskMapping.ApplyBoardFilters(Items, f, Config()).Select(i => i.Id).ToArray();

    [Fact]
    public void Sem_filtro_devolve_tudo() => Assert.Equal([101, 102, 103, 104], Apply(new DevOpsBoardFilters()));

    [Fact]
    public void Lane_padrao_e_vazio_e_lane_desconhecida_cai_na_padrao()
    {
        Assert.Equal([101, 103], Apply(new DevOpsBoardFilters { Lanes = [""] }));
        Assert.Equal([101, 103], Apply(new DevOpsBoardFilters { Lanes = ["(padrão)"] }));
        Assert.Equal([101, 103], Apply(new DevOpsBoardFilters { Lanes = ["Default"] }));
        Assert.Equal([102, 104], Apply(new DevOpsBoardFilters { Lanes = ["EXPEDITE"] }));
        Assert.Equal([102, 104], Apply(new DevOpsBoardFilters { Lanes = ["r-exp"] }));
        Assert.Equal([101, 102, 103, 104], Apply(new DevOpsBoardFilters { Lanes = ["", "Expedite"] }));
    }

    [Fact]
    public void Colunas_ocultas_tiram_os_cards_delas_pelo_nome_ou_id()
    {
        Assert.Equal([101, 103, 104], Apply(new DevOpsBoardFilters { HiddenColumns = ["new"] }));
        Assert.Equal([102], Apply(new DevOpsBoardFilters { HiddenColumns = ["c-dev", "Closed"] }));
        // Coluna que não existe mais no board não esconde nada.
        Assert.Equal([101, 102, 103, 104], Apply(new DevOpsBoardFilters { HiddenColumns = ["Coluna antiga"] }));
        Assert.Equal([104], Apply(new DevOpsBoardFilters { HiddenColumns = ["New", "Closed"], Lanes = ["Expedite"] }));
    }

    [Fact]
    public void Dentro_do_filtro_OU_entre_filtros_E()
    {
        Assert.Equal([101, 102, 103], Apply(new DevOpsBoardFilters { Types = ["bug", "User Story"] }));
        Assert.Equal([101], Apply(new DevOpsBoardFilters { Types = ["Bug"], States = ["active"] }));
        Assert.Equal([101, 104], Apply(new DevOpsBoardFilters { AssignedTo = ["ALEX@solvace.com"] }));
        Assert.Equal([103], Apply(new DevOpsBoardFilters { AssignedTo = ["__unassigned__"] }));
        Assert.Equal([103], Apply(new DevOpsBoardFilters { AssignedTo = ["(sem responsável)"] }));
        Assert.Equal([102, 103], Apply(new DevOpsBoardFilters { AssignedTo = ["maria@solvace.com", "-"] }));
    }

    [Fact]
    public void Tags_exigem_todas_as_escolhidas_como_na_tela()
    {
        Assert.Equal([101, 103, 104], Apply(new DevOpsBoardFilters { Tags = ["lindt"] }));
        Assert.Equal([101, 104], Apply(new DevOpsBoardFilters { Tags = ["Lindt", "5S"] }));
    }

    [Theory]
    [InlineData("exportacao", new[] { 101 })]
    [InlineData("RELATÓRIO", new[] { 104 })]
    [InlineData("#102", new[] { 102 })]
    [InlineData("plano kaizen", new[] { 102 })]
    [InlineData("lindt bug", new[] { 101, 103 })]
    [InlineData("nada disso", new int[0])]
    public void Texto_sem_caixa_nem_acento_em_id_titulo_e_tags(string text, int[] expected) =>
        Assert.Equal(expected, Apply(new DevOpsBoardFilters { Text = text }));

    [Fact]
    public void Filtros_limpos_lane_padrao_vazia_uma_vez()
    {
        var f = DevOpsDeskMapping.NormalizeBoardFilters(new DevOpsBoardFilters
        {
            Lanes = ["(padrão)", "", " Expedite ", "expedite", null!], Types = [" Bug ", "bug", ""], AssignedTo = ["(unassigned)"], Text = "  x  "
        });
        Assert.Equal(["", "Expedite"], f.Lanes);
        Assert.Equal(["Bug"], f.Types);
        Assert.Equal([DevOpsDeskMapping.Unassigned], f.AssignedTo);
        Assert.Equal("x", f.Text);
        Assert.True(DevOpsDeskMapping.NormalizeBoardFilters(null).IsEmpty);
    }

    [Fact]
    public void Filtros_do_epico_viram_os_do_board()
    {
        var epic = new EpicBoardFilters { Lanes = ["(padrão)"], HiddenColumns = ["Closed"], Types = ["Bug"], Tags = ["Lindt"], Text = null };
        var f = EpicCardSource.ToDevOps(epic);
        Assert.Equal([""], f.Lanes);
        Assert.Equal(["Closed"], f.HiddenColumns);
        Assert.Equal(["Bug"], f.Types);
        Assert.Equal(string.Empty, f.Text);
        Assert.True(EpicCardSource.ToDevOps(null).IsEmpty);
    }

    [Fact]
    public void Board_do_MCP_e_leitura_e_aceita_numero_ou_chave()
    {
        Assert.DoesNotContain("prmake_devops_board", CaseSpecialistMiddleware.ActingTools);
        Assert.True(CaseSpecialistMiddleware.IsAllowed("prmake_devops_board"));
        Assert.Equal(12, DevOpsMcpTools.ParseEpic("E12"));
        Assert.Equal(12, DevOpsMcpTools.ParseEpic("#12"));
        Assert.Equal(12, DevOpsMcpTools.ParseEpic(" 12 "));
        Assert.Null(DevOpsMcpTools.ParseEpic("doze"));
    }

    [Fact]
    public void Resposta_do_board_mostra_os_filtros_aplicados()
    {
        var applied = DevOpsDeskMapping.NormalizeBoardFilters(new DevOpsBoardFilters { Lanes = [""], HiddenColumns = ["c-closed"] });
        var view = JsonSerializer.SerializeToElement(DevOpsMcpTools.BoardView(Config(),
            new DevOpsBoardItemsResponse("h", DevOpsDeskMapping.ApplyBoardFilters(Items, applied, Config()), applied), 12, true, "board"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(1, view.GetProperty("count").GetInt32());
        Assert.Equal(["Closed"], view.GetProperty("hiddenColumns").EnumerateArray().Select(c => c.GetString()));
        var closed = view.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Closed");
        Assert.True(closed.GetProperty("hidden").GetBoolean());
        Assert.Equal(0, closed.GetProperty("cards").GetInt32());
        Assert.Equal(JsonValueKind.Array, view.GetProperty("filtersApplied").GetProperty("lanes").ValueKind);
        Assert.Equal(["", "Expedite"], view.GetProperty("lanes").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal("alex <alex@solvace.com>", view.GetProperty("items")[0].GetProperty("assignedTo").GetString());
    }
}
