using System.Text.Json;
using solvace.azure.application.Services;
using solvace.azure.domain.Exceptions;
using solvace.azure.domain.Models;
using solvace.prform.Execution;
using Xunit;

namespace solvace.prform.tests;

/// <summary>
/// 0099 (Mesa de gestão): mapeamento do DevOps sem o DevOps — board → configuração e hash estável, JSON Patch de
/// criar/editar/mover e tags, WIQL dos filtros e do board, árvore de áreas, guardião do caso nas ferramentas MCP.
/// </summary>
public class DevOpsDesk0099Tests
{
    private const string ColumnField = "WEF_AAA_Kanban.Column";
    private const string DoneField = "WEF_AAA_Kanban.Column.Done";
    private const string LaneField = "WEF_AAA_Kanban.Lane";

    private const string BoardJson = """
        {
          "id": "b-1", "name": "Stories",
          "columns": [
            { "id": "c-new", "name": "New", "itemLimit": 0, "columnType": "incoming", "isSplit": false, "description": "",
              "stateMappings": { "User Story": "New", "Bug": "New" } },
            { "id": "c-dev", "name": "Development", "itemLimit": 5, "columnType": "inProgress", "isSplit": true, "description": "Em desenvolvimento",
              "stateMappings": { "User Story": "Active", "Bug": "Active" } },
            { "id": "c-qa", "name": "QA", "itemLimit": 3, "columnType": "inProgress", "isSplit": false,
              "stateMappings": { "User Story": "Resolved" } },
            { "id": "c-closed", "name": "Closed", "itemLimit": 0, "columnType": "outgoing", "isSplit": false,
              "stateMappings": { "User Story": "Closed", "Bug": "Closed" } }
          ],
          "rows": [ { "id": "00000000-0000-0000-0000-000000000000", "name": null }, { "id": "r-exp", "name": "Expedite" } ],
          "fields": {
            "columnField": { "referenceName": "WEF_AAA_Kanban.Column" },
            "rowField": { "referenceName": "WEF_AAA_Kanban.Lane" },
            "doneField": { "referenceName": "WEF_AAA_Kanban.Column.Done" }
          }
        }
        """;

    private static readonly Dictionary<string, string?> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bug"] = "#CC293D", ["User Story"] = "#009CCC"
    };

    private static DevOpsBoardConfig Board(string json = BoardJson)
    {
        using var doc = JsonDocument.Parse(json);
        return DevOpsDeskMapping.ParseBoard(doc.RootElement, "Time A", Colors, "Microsoft.VSTS.Common.StackRank");
    }

    private static Dictionary<string, JsonElement> Fields(object values) =>
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    private static Dictionary<string, JsonElement> CardFields(string type, string state, string? column = null, bool done = false, string? lane = null,
        string? tags = null)
    {
        var d = new Dictionary<string, object?> { ["System.WorkItemType"] = type, ["System.State"] = state, ["System.Title"] = "t" };
        if (column is not null) d[ColumnField] = column;
        if (done) d[DoneField] = true;
        if (lane is not null) d[LaneField] = lane;
        if (tags is not null) d["System.Tags"] = tags;
        return Fields(d);
    }

    // ---------------------------------------------------------------- board

    [Fact]
    public void Board_vira_configuracao_igual_ao_DevOps()
    {
        var config = Board();

        Assert.Equal("Stories", config.Board);
        Assert.Equal("Time A", config.Team);
        Assert.Equal(["New", "Development", "QA", "Closed"], config.Columns.Select(c => c.Name));
        Assert.Equal(["incoming", "inProgress", "inProgress", "outgoing"], config.Columns.Select(c => c.ColumnType));
        Assert.True(config.Columns[1].IsSplit);
        Assert.Equal(5, config.Columns[1].ItemLimit);
        Assert.Equal("Em desenvolvimento", config.Columns[1].Description);
        Assert.Null(config.Columns[0].Description);
        Assert.Equal("Active", config.Columns[1].StateMappings["bug"]);
        Assert.False(config.Columns[2].StateMappings.ContainsKey("Bug"));

        Assert.Equal(2, config.Lanes.Count);
        Assert.True(config.Lanes[0].IsDefault);
        Assert.Equal(string.Empty, config.Lanes[0].Name);
        Assert.Equal("Expedite", config.Lanes[1].Name);
        Assert.False(config.Lanes[1].IsDefault);

        Assert.Equal(new DevOpsBoardFields(ColumnField, LaneField, DoneField), config.Fields);
        Assert.Equal(["Bug", "User Story"], config.Types.Select(t => t.Name));
        Assert.Equal("#CC293D", config.Types[0].Color);
        Assert.Equal("Microsoft.VSTS.Common.StackRank", config.OrderField);
        Assert.Matches("^[0-9a-f]{16}$", config.ConfigHash);
    }

    [Fact]
    public void ConfigHash_estavel_e_muda_com_a_configuracao()
    {
        var a = Board();
        var b = Board();
        Assert.Equal(a.ConfigHash, b.ConfigHash);

        // Mesmo mapeamento em outra ordem de chaves = mesmo hash.
        var reordered = BoardJson.Replace("\"User Story\": \"New\", \"Bug\": \"New\"", "\"Bug\": \"New\", \"User Story\": \"New\"");
        Assert.NotEqual(BoardJson, reordered);
        Assert.Equal(a.ConfigHash, Board(reordered).ConfigHash);

        // Limite WIP, coluna nova, lane nova ou estado novo = hash novo (a tela redesenha).
        Assert.NotEqual(a.ConfigHash, Board(BoardJson.Replace("\"itemLimit\": 5", "\"itemLimit\": 6")).ConfigHash);
        Assert.NotEqual(a.ConfigHash, Board(BoardJson.Replace("\"name\": \"Expedite\"", "\"name\": \"Urgente\"")).ConfigHash);
        Assert.NotEqual(a.ConfigHash, Board(BoardJson.Replace("\"User Story\": \"Resolved\"", "\"User Story\": \"Resolved\", \"Bug\": \"Resolved\"")).ConfigHash);
    }

    [Fact]
    public void Coluna_em_numero_vira_o_tipo_do_contrato()
    {
        var config = Board(BoardJson.Replace("\"columnType\": \"incoming\"", "\"columnType\": 0").Replace("\"columnType\": \"outgoing\"", "\"columnType\": 2"));
        Assert.Equal("incoming", config.Columns[0].ColumnType);
        Assert.Equal("outgoing", config.Columns[3].ColumnType);
    }

    // ---------------------------------------------------------------- mover

    private static Dictionary<string, object?> Ops(DevOpsDeskMapping.MovePlan plan) =>
        plan.Ops.ToDictionary(o => $"{o.Op} {o.Path}", o => o.Value);

    [Fact]
    public void Mover_grava_coluna_estado_do_tipo_done_e_lane()
    {
        var plan = DevOpsDeskMapping.BuildMove(Board(), "Bug", "New", CardFields("Bug", "New", "New"), "development", "Expedite", true);

        var ops = Ops(plan);
        Assert.Equal("Development", ops[$"add /fields/{ColumnField}"]);
        Assert.Equal("Active", ops["add /fields/System.State"]);
        Assert.Equal(true, ops[$"add /fields/{DoneField}"]);
        Assert.Equal("Expedite", ops[$"add /fields/{LaneField}"]);
        Assert.Equal("Active", plan.State);
        Assert.True(plan.Done);
    }

    [Fact]
    public void Mover_pelo_id_da_coluna_e_sair_do_done_limpa_o_done()
    {
        var plan = DevOpsDeskMapping.BuildMove(Board(), "User Story", "Active", CardFields("User Story", "Active", "Development", done: true), "c-qa", null, null);

        var ops = Ops(plan);
        Assert.Equal("QA", ops[$"add /fields/{ColumnField}"]);
        Assert.Equal("Resolved", ops["add /fields/System.State"]);
        Assert.Equal(false, ops[$"add /fields/{DoneField}"]);
        Assert.DoesNotContain(plan.Ops, o => o.Path.EndsWith("Lane"));
    }

    [Fact]
    public void Mover_para_a_lane_padrao_limpa_a_lane_so_se_tinha()
    {
        var withLane = DevOpsDeskMapping.BuildMove(Board(), "Bug", "Active", CardFields("Bug", "Active", "Development", lane: "Expedite"), "Development", "default", null);
        Assert.Contains(withLane.Ops, o => o.Op == "remove" && o.Path == $"/fields/{LaneField}");
        Assert.Equal(string.Empty, withLane.Lane);

        var noLane = DevOpsDeskMapping.BuildMove(Board(), "Bug", "Active", CardFields("Bug", "Active", "Development"), "Development", "", null);
        Assert.Empty(noLane.Ops);
    }

    [Fact]
    public void Reordenar_na_mesma_coluna_nao_gera_revisao()
    {
        var plan = DevOpsDeskMapping.BuildMove(Board(), "Bug", "Active", CardFields("Bug", "Active", "Development"), "Development", null, null);
        Assert.Empty(plan.Ops);
    }

    [Fact]
    public void Coluna_sem_mapeamento_do_tipo_recusa_com_409()
    {
        var e = Assert.Throws<DevOpsDeskException>(() =>
            DevOpsDeskMapping.BuildMove(Board(), "Bug", "Active", CardFields("Bug", "Active", "Development"), "QA", null, null));
        Assert.Equal(409, e.StatusCode);
        Assert.Equal("no_state_mapping", e.Code);
        Assert.Contains("QA", e.Message);

        var missing = Assert.Throws<DevOpsDeskException>(() =>
            DevOpsDeskMapping.BuildMove(Board(), "Bug", "Active", CardFields("Bug", "Active"), "Inexistente", null, null));
        Assert.Equal(400, missing.StatusCode);
    }

    // ---------------------------------------------------------------- tags e patch

    [Fact]
    public void Tags_somadas_e_tiradas_ignoram_caixa()
    {
        var merged = DevOpsDeskMapping.MergeTags(DevOpsDeskMapping.SplitTags("Alpha; beta;;gamma "), ["BETA", "delta", " "], ["GAMMA"]);
        Assert.Equal(["Alpha", "beta", "delta"], merged);
        Assert.Equal("Alpha; beta; delta", DevOpsDeskMapping.JoinTags(merged));
    }

    [Fact]
    public void Editar_monta_patch_de_campos_e_tags()
    {
        var current = CardFields("Bug", "Active", tags: "Alpha; beta");
        current["Microsoft.VSTS.Common.Priority"] = JsonSerializer.SerializeToElement(2);

        var ops = DevOpsDeskMapping.BuildUpdatePatch(current,
            Fields(new Dictionary<string, object?>
            {
                ["System.Title"] = "Novo", ["Microsoft.VSTS.Common.Priority"] = null, ["Custom.Vazio"] = null
            }),
            ["gamma"], ["alpha"]);

        Assert.Contains(ops, o => o.Op == "add" && o.Path == "/fields/System.Title" && ((JsonElement)o.Value!).GetString() == "Novo");
        Assert.Contains(ops, o => o.Op == "remove" && o.Path == "/fields/Microsoft.VSTS.Common.Priority" && o.Value is null);
        Assert.DoesNotContain(ops, o => o.Path == "/fields/Custom.Vazio"); // não tinha valor: nada a limpar
        Assert.Contains(ops, o => o.Path == "/fields/System.Tags" && (string)o.Value! == "beta; gamma");

        // Tags que não mudam não entram.
        Assert.Empty(DevOpsDeskMapping.BuildUpdatePatch(current, null, ["ALPHA"], ["inexistente"]));
    }

    [Fact]
    public void Patch_serializa_como_o_DevOps_espera()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new DevOpsPatchOp("add", "/fields/System.Title", "x"), new DevOpsPatchOp("remove", "/fields/System.AssignedTo")
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        Assert.Equal("""[{"op":"add","path":"/fields/System.Title","value":"x"},{"op":"remove","path":"/fields/System.AssignedTo"}]""", json);
    }

    [Fact]
    public void Criar_monta_titulo_campos_tags_e_pai()
    {
        var ops = DevOpsDeskMapping.BuildCreatePatch("  Título  ",
            Fields(new Dictionary<string, object?> { ["System.AreaPath"] = "Proj\\Time A", ["System.Tags"] = "um", ["Custom.Nada"] = null }),
            ["dois", "UM"], 123, "https://dev.azure.com/org");

        Assert.Equal("Título", ops.Single(o => o.Path == "/fields/System.Title").Value);
        Assert.Equal("Proj\\Time A", ((JsonElement)ops.Single(o => o.Path == "/fields/System.AreaPath").Value!).GetString());
        Assert.Equal("um; dois", ops.Single(o => o.Path == "/fields/System.Tags").Value);
        Assert.DoesNotContain(ops, o => o.Path == "/fields/Custom.Nada");
        var parent = ops.Single(o => o.Path == "/relations/-");
        var rel = JsonSerializer.SerializeToElement(parent.Value);
        Assert.Equal("System.LinkTypes.Hierarchy-Reverse", rel.GetProperty("rel").GetString());
        Assert.Equal("https://dev.azure.com/org/_apis/wit/workItems/123", rel.GetProperty("url").GetString());

        Assert.Throws<DevOpsDeskException>(() => DevOpsDeskMapping.BuildCreatePatch(" ", null, null, null, "https://dev.azure.com/org"));
        Assert.Throws<DevOpsDeskException>(() => DevOpsDeskMapping.BuildCreatePatch("x",
            Fields(new Dictionary<string, object?> { ["/fields/x"] = "y" }), null, null, "https://dev.azure.com/org"));
    }

    // ---------------------------------------------------------------- WIQL

    [Fact]
    public void Filtros_viram_WIQL()
    {
        var wiql = DevOpsDeskMapping.BuildFilterWiql(new DevOpsQueryFilter
        {
            Types = ["Bug"], States = ["Active", "New"], Tags = ["cliente x", "urgente"], AssignedTo = ["@me", "ana@solvace.com"],
            Text = "d'água", AreaPath = "Proj\\Time A", CreatedFrom = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            ChangedFrom = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero)
        });

        Assert.StartsWith("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project", wiql);
        Assert.Contains("[System.WorkItemType] IN ('Bug')", wiql);
        Assert.Contains("[System.State] IN ('Active', 'New')", wiql);
        Assert.Contains("([System.AssignedTo] = @me OR [System.AssignedTo] = 'ana@solvace.com')", wiql);
        Assert.Contains("([System.Tags] CONTAINS 'cliente x' OR [System.Tags] CONTAINS 'urgente')", wiql);
        Assert.Contains("[System.Title] CONTAINS 'd''água'", wiql);
        Assert.Contains("[System.AreaPath] UNDER 'Proj\\Time A'", wiql);
        Assert.Contains("[System.CreatedDate] >= '2026-11-01'", wiql);
        Assert.Contains("[System.ChangedDate] >= '2026-11-20'", wiql);
        Assert.EndsWith("ORDER BY [System.ChangedDate] DESC", wiql);

        Assert.Contains("([System.Id] = 78720 OR [System.Title] CONTAINS '78720')", DevOpsDeskMapping.BuildFilterWiql(new DevOpsQueryFilter { Text = "#78720" }));
        Assert.Equal("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.ChangedDate] DESC",
            DevOpsDeskMapping.BuildFilterWiql(null));
    }

    [Fact]
    public void WIQL_do_board_por_area_do_time_tipos_e_estados_das_colunas()
    {
        var config = Board();
        var open = DevOpsDeskMapping.BuildBoardWiql(config, "System.AreaPath", [("Proj\\Time A", true), ("Proj\\Outro", false)], null, false,
            new DevOpsBoardItemsRequest { Tags = ["x"] })!;

        Assert.Contains("([System.AreaPath] UNDER 'Proj\\Time A' OR [System.AreaPath] = 'Proj\\Outro')", open);
        Assert.Contains("([System.WorkItemType] = 'Bug' AND [System.State] IN ('New', 'Active'))", open);
        Assert.Contains("([System.WorkItemType] = 'User Story' AND [System.State] IN ('New', 'Active', 'Resolved'))", open);
        Assert.DoesNotContain("'Closed'", open);
        Assert.Contains("[System.Tags] CONTAINS 'x'", open);
        Assert.EndsWith("ORDER BY [Microsoft.VSTS.Common.StackRank] ASC, [System.Id] ASC", open);

        var closed = DevOpsDeskMapping.BuildBoardWiql(config, "System.AreaPath", [("Proj\\Time A", true)], null, true, new DevOpsBoardItemsRequest())!;
        Assert.Contains("([System.WorkItemType] = 'Bug' AND [System.State] IN ('Closed'))", closed);
        Assert.EndsWith("ORDER BY [System.ChangedDate] DESC", closed);

        var epic = DevOpsDeskMapping.BuildBoardWiql(config, null, [], [10, 20, 10], null, new DevOpsBoardItemsRequest())!;
        Assert.Contains("[System.Id] IN (10, 20)", epic);
        Assert.DoesNotContain("AreaPath", epic);
        Assert.Null(DevOpsDeskMapping.BuildBoardWiql(config, null, [], [], null, new DevOpsBoardItemsRequest()));
    }

    [Fact]
    public void Ids_da_WIQL_plana_e_de_links()
    {
        using var flat = JsonDocument.Parse("""{"workItems":[{"id":3},{"id":1},{"id":3}]}""");
        Assert.Equal([3, 1], DevOpsDeskMapping.ParseWiqlIds(flat.RootElement));
        using var links = JsonDocument.Parse("""{"workItemRelations":[{"target":{"id":5}},{"source":{"id":5},"target":{"id":6}}]}""");
        Assert.Equal([5, 6], DevOpsDeskMapping.ParseWiqlIds(links.RootElement));
    }

    // ---------------------------------------------------------------- leituras

    [Fact]
    public void Cartao_do_board_pega_coluna_do_campo_ou_do_estado()
    {
        var config = Board();
        using var doc = JsonDocument.Parse("""
            {"id": 7, "rev": 3, "fields": {
              "System.WorkItemType": "Bug", "System.Title": "Erro", "System.State": "Active", "System.Tags": "a; b",
              "System.AssignedTo": {"displayName": "Ana", "uniqueName": "ana@x.com", "imageUrl": "http://img"},
              "WEF_AAA_Kanban.Column.Done": true, "Microsoft.VSTS.Common.StackRank": 12.5, "Microsoft.VSTS.Common.Priority": 2,
              "System.Parent": 3, "System.ChangedDate": "2026-11-24T10:00:00Z" }}
            """);
        var item = DevOpsDeskMapping.ParseBoardItem(doc.RootElement, config);

        Assert.Equal("Development", item.Column); // sem o campo do board: a coluna do estado
        Assert.True(item.Done);
        Assert.Equal(string.Empty, item.Lane);
        Assert.Equal(["a", "b"], item.Tags);
        Assert.Equal("ana@x.com", item.AssignedTo!.UniqueName);
        Assert.Equal(12.5, item.Order);
        Assert.Equal(2, item.Priority);
        Assert.Equal(3, item.ParentId);
        Assert.Equal(3, item.Rev);
    }

    private const string CardRulesJson = """
        {"rules": {
          "fill": [
            { "name": "Bloqueado", "isEnabled": "true", "filter": "[System.Tags] contains 'BLOCK'",
              "clauses": [ { "fieldName": "System.Tags", "index": 1, "logicalOperator": "", "operator": "CONTAINS", "value": "BLOCK" } ],
              "settings": { "background-color": "#C9B3E0", "title-color": "#000000" } },
            { "name": "Desligada", "isEnabled": "false",
              "clauses": [ { "fieldName": "System.State", "index": 1, "logicalOperator": "", "operator": "=", "value": "Active" } ],
              "settings": { "background-color": "#000000" } },
            { "name": "Crítico", "isEnabled": "true",
              "clauses": [ { "fieldName": "Microsoft.VSTS.Common.Severity", "index": 1, "logicalOperator": "", "operator": "=", "value": "1 - Critical" } ],
              "settings": { "background-color": "#F4A6A6" } },
            { "name": "Alto", "isEnabled": "true",
              "clauses": [
                { "fieldName": "Microsoft.VSTS.Common.Severity", "index": 1, "logicalOperator": "", "operator": "=", "value": "2 - High" },
                { "fieldName": "System.WorkItemType", "index": 2, "logicalOperator": "And", "operator": "=", "value": "Bug" },
                { "fieldName": "Microsoft.VSTS.Common.Priority", "index": 3, "logicalOperator": "Or", "operator": "<=", "value": "0" } ],
              "settings": { "background-color": "FFFBE88A" } },
            { "name": "Meu", "isEnabled": "true",
              "clauses": [ { "fieldName": "System.AssignedTo", "index": 1, "logicalOperator": "", "operator": "=", "value": "@Me" } ],
              "settings": { "background-color": "#00FF00" } }
          ],
          "tagStyle": [
            { "name": "BLOCK", "isEnabled": "true", "settings": { "background-color": "#E0262E", "color": "#FFFFFF" } },
            { "name": "velha", "isEnabled": "false", "settings": { "background-color": "#000000", "color": "#FFFFFF" } }
          ]
        }}
        """;

    [Fact]
    public void Estilos_do_board_pintam_o_card_como_o_DevOps()
    {
        var config = Board();
        var before = config.ConfigHash;
        using (var doc = JsonDocument.Parse(CardRulesJson))
            DevOpsDeskMapping.ApplyCardRules(config, doc.RootElement);

        Assert.Equal(["Bloqueado", "Crítico", "Alto", "Meu"], config.CardRules.Select(r => r.Name));
        Assert.Equal("#FBE88A", config.CardRules[2].Background); // ARGB sem # → #RGB
        Assert.Equal([new DevOpsTagStyle("BLOCK", "#E0262E", "#FFFFFF")], config.TagStyles);
        Assert.NotEqual(before, config.ConfigHash);
        Assert.Contains("Microsoft.VSTS.Common.Severity", DevOpsDeskMapping.BoardItemFields(config));

        DevOpsBoardItem Card(string fields)
        {
            using var doc = JsonDocument.Parse("""{"id": 1, "rev": 1, "fields": {""" + fields + "}}");
            return DevOpsDeskMapping.ParseBoardItem(doc.RootElement, config);
        }

        // BLOCK vem antes da severidade (a regra de cima vence) e a tag compara inteira, sem caixa.
        var blocked = Card("""
            "System.WorkItemType": "Bug", "System.State": "Active", "System.Tags": "CAB; block",
            "Microsoft.VSTS.Common.Severity": "1 - Critical"
            """);
        Assert.Equal("#C9B3E0", blocked.CardColor);
        Assert.Equal("#000000", blocked.TitleColor);
        Assert.Equal("#F4A6A6", Card("""
            "System.WorkItemType": "Bug", "System.State": "Active", "System.Tags": "BLOCKED", "Microsoft.VSTS.Common.Severity": "1 - Critical"
            """).CardColor);
        Assert.Equal("#FBE88A", Card("""
            "System.WorkItemType": "Bug", "System.State": "New", "Microsoft.VSTS.Common.Severity": "2 - High"
            """).CardColor);
        // AND antes de OR: High sem ser Bug não pinta; prioridade 0 pinta sozinha.
        Assert.Null(Card("""
            "System.WorkItemType": "User Story", "System.State": "New", "Microsoft.VSTS.Common.Severity": "2 - High", "Microsoft.VSTS.Common.Priority": 2
            """).CardColor);
        Assert.Equal("#FBE88A", Card("""
            "System.WorkItemType": "User Story", "System.State": "New", "Microsoft.VSTS.Common.Priority": 0
            """).CardColor);
        // Regra desligada e macro (@Me) não pintam.
        Assert.Null(Card("""
            "System.WorkItemType": "Bug", "System.State": "Active", "System.AssignedTo": {"displayName": "Ana", "uniqueName": "ana@x.com"}
            """).CardColor);

        var plain = DevOpsDeskMapping.WithoutCardRules(config);
        Assert.Empty(plain.CardRules);
        Assert.Single(plain.TagStyles);
        Assert.Equal(4, config.CardRules.Count);
    }

    [Fact]
    public void Card_com_relacoes()
    {
        using var doc = JsonDocument.Parse("""
            {"id": 9, "rev": 1, "fields": {"System.WorkItemType": "User Story", "System.Title": "US", "System.State": "New"},
             "relations": [
               {"rel": "System.LinkTypes.Hierarchy-Reverse", "url": "https://dev.azure.com/org/_apis/wit/workItems/4", "attributes": {"name": "Parent"}},
               {"rel": "AttachedFile", "url": "https://dev.azure.com/org/_apis/wit/attachments/abc?fileName=x.png", "attributes": {"name": "x.png"}}
             ]}
            """);
        var card = DevOpsDeskMapping.ParseCard(doc.RootElement, "https://dev.azure.com/org/p/_workitems/edit/9");
        Assert.Equal("User Story", card.Type);
        Assert.Equal(4, card.Relations[0].Id);
        Assert.Equal("Parent", card.Relations[0].Name);
        Assert.Null(card.Relations[1].Id);
    }

    [Fact]
    public void Areas_achatadas_no_formato_do_campo()
    {
        using var doc = JsonDocument.Parse("""
            {"value": [
              {"structureType": "area", "name": "Proj", "children": [{"name": "Time A", "children": [{"name": "Sub"}]}, {"name": "Time B"}]},
              {"structureType": "iteration", "name": "Proj", "children": [{"name": "Sprint 1"}]}
            ]}
            """);
        var areas = DevOpsDeskMapping.FlattenNodes(doc.RootElement, "area");
        Assert.Equal(["Proj", "Proj\\Time A", "Proj\\Time A\\Sub", "Proj\\Time B"], areas.Select(a => a.Path));
        Assert.Equal([0, 1, 2, 1], areas.Select(a => a.Depth));
        Assert.Equal(["Proj", "Proj\\Sprint 1"], DevOpsDeskMapping.FlattenNodes(doc.RootElement, "iteration").Select(a => a.Path));
    }

    [Fact]
    public void Campos_do_tipo_com_picklist_e_definicao_do_projeto()
    {
        using var project = JsonDocument.Parse("""
            {"value": [
              {"referenceName": "System.State", "type": "string", "readOnly": false, "isPicklist": false},
              {"referenceName": "Custom.BugType", "type": "string", "isPicklist": true},
              {"referenceName": "Microsoft.VSTS.Common.Priority", "type": "integer", "isPicklist": false},
              {"referenceName": "System.Id", "type": "integer", "readOnly": true},
              {"referenceName": "System.AssignedTo", "type": "identity"}
            ]}
            """);
        using var typeFields = JsonDocument.Parse("""
            {"value": [
              {"referenceName": "Custom.BugType", "name": "Bug Type", "alwaysRequired": true, "allowedValues": ["Performance", "Report"]},
              {"referenceName": "Microsoft.VSTS.Common.Priority", "name": "Priority", "allowedValues": [1, 2, 3], "defaultValue": 2},
              {"referenceName": "System.Id", "name": "ID", "allowedValues": []},
              {"referenceName": "System.AssignedTo", "name": "Assigned To", "allowedValues": []}
            ]}
            """);
        var fields = DevOpsDeskMapping.ParseTypeFields(typeFields.RootElement, DevOpsDeskMapping.ParseProjectFields(project.RootElement));

        Assert.Equal(new[] { "picklistString", "integer", "integer", "identity" }, fields.Select(f => f.Type));
        Assert.True(fields[0].Required);
        Assert.Equal(["Performance", "Report"], fields[0].AllowedValues);
        Assert.Equal(["1", "2", "3"], fields[1].AllowedValues);
        Assert.Equal("2", fields[1].DefaultValue);
        Assert.True(fields[2].ReadOnly);
        Assert.Equal("dateTime", DevOpsDeskMapping.MapFieldType("dateTime", false));
        Assert.Equal("picklistInteger", DevOpsDeskMapping.MapFieldType("integer", true));
        Assert.Equal("html", DevOpsDeskMapping.MapFieldType("history", false));
    }

    [Fact]
    public void Erro_do_DevOps_vira_status_e_codigo()
    {
        Assert.Equal((400, "devops_rejected"), Pair(DevOpsDeskException.FromDevOps(400, "TF401320: Rule Error")));
        Assert.Equal((403, "devops_forbidden"), Pair(DevOpsDeskException.FromDevOps(401, "x")));
        Assert.Equal((403, "devops_forbidden"), Pair(DevOpsDeskException.FromDevOps(403, "x")));
        Assert.Equal((404, "devops_not_found"), Pair(DevOpsDeskException.FromDevOps(404, "x")));
        Assert.Equal((409, "devops_conflict"), Pair(DevOpsDeskException.FromDevOps(412, "x")));
        Assert.Equal((502, "devops_error"), Pair(DevOpsDeskException.FromDevOps(500, "x")));

        static (int, string) Pair(DevOpsDeskException e) => (e.StatusCode, e.Code);
    }

    // ---------------------------------------------------------------- MCP / guardião

    [Theory]
    [InlineData(null, 5, false, null, true)]          // fora do token do caso
    [InlineData("E12", 5, false, null, true)]         // épico: qualquer card
    [InlineData("E12", null, true, null, true)]       // épico: cria solto
    [InlineData("78720", 78720, false, null, true)]   // card: o próprio
    [InlineData("78720", 5, false, null, false)]      // card: outro
    [InlineData("78720", null, true, 78720, true)]    // card: cria filho dele
    [InlineData("78720", null, true, null, false)]    // card: cria solto
    [InlineData("78720", null, true, 5, false)]       // card: cria filho de outro
    public void Guardiao_do_caso_nas_gravacoes(string? own, int? target, bool create, int? parentId, bool allowed)
    {
        Assert.Equal(allowed, DevOpsMcpTools.CaseScopeError(own, target, create, parentId) is null);
    }

    [Fact]
    public void Gravacoes_do_DevOps_rodam_como_quem_pediu()
    {
        foreach (var tool in new[] { "prmake_devops_card_create", "prmake_devops_card_update", "prmake_devops_card_move" })
        {
            Assert.Contains(tool, CaseSpecialistMiddleware.ActingTools);
            Assert.True(CaseSpecialistMiddleware.IsAllowed(tool));
        }
        Assert.DoesNotContain("prmake_devops_query", CaseSpecialistMiddleware.ActingTools);
        Assert.True(DevOpsMcpTools.IsEpicKey("E1234"));
        Assert.False(DevOpsMcpTools.IsEpicKey("78720"));
    }

    [Fact]
    public void Fields_do_MCP_em_objeto_ou_texto()
    {
        var fromObject = DevOpsMcpTools.FieldMap(JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["System.Title"] = "a", ["X.Y"] = null }))!;
        Assert.Equal("a", fromObject["System.Title"].GetString());
        Assert.Equal(JsonValueKind.Null, fromObject["X.Y"].ValueKind);

        var fromText = DevOpsMcpTools.FieldMap(JsonSerializer.SerializeToElement("""{"Microsoft.VSTS.Common.Priority": 1}"""))!;
        Assert.Equal(1, fromText["Microsoft.VSTS.Common.Priority"].GetInt32());
        Assert.Null(DevOpsMcpTools.FieldMap(null));
    }
}
