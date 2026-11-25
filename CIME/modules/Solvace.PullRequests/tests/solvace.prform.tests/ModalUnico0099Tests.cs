using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using solvace.azure.application.Services;
using solvace.azure.domain.Models;
using solvace.prform.Controllers;
using solvace.prform.Execution;
using Xunit;

namespace solvace.prform.tests;

/// <summary>
/// 0099 (modal único do card): a Bancada abre o modal da Mesa — endpoints do card valem para dev; histórico do card; board em
/// que o card aparece.
/// </summary>
public class ModalUnico0099Tests
{
    // ---------------------------------------------------------------- acesso

    private static IActionResult? Authorize(string[] roles, MethodInfo action)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test")) };
        http.Request.Method = "GET";
        var descriptor = new ActionDescriptor { EndpointMetadata = action.GetCustomAttributes(true).ToList() };
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), []);
        new DeskAccessAttribute().OnAuthorization(context);
        return context.Result;
    }

    private static MethodInfo Action(string name) => typeof(DevOpsController).GetMethod(name)!;

    [Theory]
    [InlineData(nameof(DevOpsController.Card))]
    [InlineData(nameof(DevOpsController.Update))]
    [InlineData(nameof(DevOpsController.Comment))]
    [InlineData(nameof(DevOpsController.Comments))]
    [InlineData(nameof(DevOpsController.Attachments))]
    [InlineData(nameof(DevOpsController.History))]
    [InlineData(nameof(DevOpsController.CardBoard))]
    [InlineData(nameof(DevOpsController.Move))]
    [InlineData(nameof(DevOpsController.Fields))]
    [InlineData(nameof(DevOpsController.People))]
    public void Dev_reaches_the_card_endpoints(string action)
    {
        Assert.Null(Authorize(["user"], Action(action)));
        Assert.Null(Authorize(["support"], Action(action)));
    }

    [Theory]
    [InlineData(nameof(DevOpsController.Create))]
    [InlineData(nameof(DevOpsController.BoardItems))]
    [InlineData(nameof(DevOpsController.Teams))]
    [InlineData(nameof(DevOpsController.Boards))]
    public void Desk_only_endpoints_stay_closed_to_dev(string action) =>
        Assert.NotNull(Authorize(["user"], Action(action)));

    [Fact]
    public void Card_endpoints_need_a_known_role() =>
        Assert.NotNull(Authorize(["gestor"], Action(nameof(DevOpsController.Card))));

    // ---------------------------------------------------------------- histórico

    private const string Updates = """
    {"value":[
      {"rev":1,"revisedBy":{"displayName":"Ana Souza","uniqueName":"ana@x.com","id":"g1"},
       "fields":{"System.Id":{"newValue":78794},"System.Title":{"newValue":"Erro ao salvar"},"System.State":{"newValue":"New"},
                 "System.ChangedDate":{"newValue":"2026-11-20T10:00:00Z"},"System.Rev":{"newValue":1}}},
      {"rev":2,"revisedBy":{"displayName":"Bruno Lima","uniqueName":"bruno@x.com","id":"g2"},
       "fields":{"System.State":{"oldValue":"New","newValue":"Test QA"},
                 "System.AssignedTo":{"oldValue":{"displayName":"Ana Souza"},"newValue":{"displayName":"Bruno Lima"}},
                 "WEF_ABC_Kanban.Column":{"oldValue":"Ready","newValue":"QA Done"},
                 "System.BoardColumn":{"oldValue":"Ready","newValue":"QA Done"},
                 "System.History":{"newValue":"<div>Validado</div>"},
                 "System.ChangedDate":{"oldValue":"2026-11-20T10:00:00Z","newValue":"2026-11-21T12:30:00Z"},
                 "System.Watermark":{"oldValue":1,"newValue":2}}},
      {"rev":3,"revisedBy":{"displayName":"Bruno Lima"},
       "relations":{"added":[{"rel":"System.LinkTypes.Related","url":"https://dev.azure.com/o/_apis/wit/workItems/90002","attributes":{"isLocked":false}}],
                    "removed":[{"rel":"Hyperlink","url":"https://x.com","attributes":{"name":"doc"}}]}},
      {"rev":4,"fields":{"System.Watermark":{"oldValue":2,"newValue":3},"System.Rev":{"oldValue":3,"newValue":4}}}
    ]}
    """;

    [Fact]
    public void History_page_count_includes_noise_only_revisions()
    {
        // A paginação ($top=200, limite do DevOps) para quando a página vem menor — conta tudo o que veio, não só o que aparece.
        using var doc = JsonDocument.Parse(Updates);
        Assert.Equal(4, DevOpsDeskMapping.HistoryPageCount(doc.RootElement));
        Assert.Equal(3, DevOpsDeskMapping.ParseHistory(doc.RootElement).Count);
        using var empty = JsonDocument.Parse("{}");
        Assert.Equal(0, DevOpsDeskMapping.HistoryPageCount(empty.RootElement));
    }

    [Fact]
    public void History_is_newest_first_with_readable_changes()
    {
        using var doc = JsonDocument.Parse(Updates);
        var list = DevOpsDeskMapping.ParseHistory(doc.RootElement);

        Assert.Equal([3, 2, 1], list.Select(e => e.Rev));
        var created = list[2];
        Assert.True(created.Created);
        Assert.Equal("Ana Souza", created.By!.DisplayName);
        Assert.DoesNotContain(created.Changes, c => c.Field is "System.Id" or "System.Rev" or "System.ChangedDate");

        var moved = list[1];
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 12, 30, 0, TimeSpan.Zero), moved.Date);
        Assert.Equal("<div>Validado</div>", moved.Comment);
        Assert.Contains(moved.Changes, c => c is { Field: "System.State", OldValue: "New", NewValue: "Test QA" });
        Assert.Contains(moved.Changes, c => c is { Field: "System.AssignedTo", OldValue: "Ana Souza", NewValue: "Bruno Lima" });
        Assert.Contains(moved.Changes, c => c.Field == "System.BoardColumn");
        Assert.DoesNotContain(moved.Changes, c => c.Field.StartsWith("WEF_") || c.Field is "System.History" or "System.Watermark");

        var links = list[0];
        Assert.Equal(90002, Assert.Single(links.LinksAdded).Id);
        Assert.Equal("doc", Assert.Single(links.LinksRemoved).Name);
    }

    // ---------------------------------------------------------------- board do card

    private static Dictionary<string, JsonElement> Fields(params (string Key, object Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));

    [Fact]
    public void Active_board_field_is_the_one_with_the_extension_marker()
    {
        var fields = Fields(("WEF_OLD_Kanban.Column", "Ready"), ("WEF_NEW_Kanban.Column", "QA Done"), ("WEF_NEW_System.ExtensionMarker", true),
            ("System.BoardColumn", "Ready"));
        Assert.Equal("WEF_NEW_Kanban.Column", DevOpsDeskMapping.ActiveBoardColumnField(fields));
        Assert.Null(DevOpsDeskMapping.ActiveBoardColumnField(Fields(("System.State", "New"))));
    }

    [Fact]
    public void Teams_of_the_card_area_are_searched_first()
    {
        var teams = new List<DevOpsNamedRef> { new("1", "Alpha"), new("2", "Product Development Team"), new("3", "Solvace"), new("4", "Squad Sustain") };
        var order = DevOpsDeskMapping.TeamsByArea(teams, "Solvace\\Product Development Team", "Squad Sustain").Select(t => t.Name).ToList();
        Assert.Equal(["Product Development Team", "Solvace", "Squad Sustain", "Alpha"], order);
    }
}
