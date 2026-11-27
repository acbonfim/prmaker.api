using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

/// <summary>0099: contagens do épico — cards do DevOps, itens de trabalho e itens ainda abertos.</summary>
public class EpicCounts
{
    public int Cards { get; set; }
    public int Items { get; set; }
    public int OpenItems { get; set; }
}

/// <summary>0099: épico na lista da Mesa de gestão (JSON camelCase). <c>key</c> = <c>E{number}</c>.</summary>
public class EpicSummary
{
    public Guid Id { get; set; }
    public long Number { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = EpicStatus.Active;
    public string Color { get; set; } = Epic.DefaultColor;
    public List<string> Tags { get; set; } = [];
    public string? DevOpsTeam { get; set; }
    public string? DevOpsBoard { get; set; }
    public string? DevOpsCardId { get; set; }
    public Guid? CreatedBy { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public EpicCounts Counts { get; set; } = new();
    /// <summary>Ajustes 1: filtros do board salvos no épico (sempre objeto; listas vazias por padrão).</summary>
    public EpicBoardFilters BoardFilters { get; set; } = new() { Text = string.Empty };
    /// <summary>0104: rascunhos de histórias pelos cards do épico.</summary>
    public bool AllowCardStoryDrafts { get; set; } = true;

    public static T Fill<T>(T target, Epic epic, EpicCounts? counts) where T : EpicSummary
    {
        target.Id = epic.Id;
        target.Number = epic.Number;
        target.Key = epic.Key;
        target.Title = epic.Title;
        target.Status = epic.Status;
        target.Color = epic.Color;
        target.Tags = epic.Tags.ToList();
        target.DevOpsTeam = epic.DevOpsTeam;
        target.DevOpsBoard = epic.DevOpsBoard;
        target.DevOpsCardId = epic.DevOpsCardId;
        target.CreatedBy = epic.CreatedBy;
        target.CreatedByName = epic.CreatedByName;
        target.CreatedAt = epic.CreatedAt;
        target.UpdatedAt = epic.UpdatedAt;
        target.ClosedAt = epic.ClosedAt;
        target.Counts = counts ?? new EpicCounts { Cards = epic.Cards.Count };
        target.BoardFilters = epic.BoardFilters;
        target.AllowCardStoryDrafts = epic.AllowCardStoryDrafts;
        return target;
    }

    public static EpicSummary From(Epic epic, EpicCounts? counts) => Fill(new EpicSummary(), epic, counts);
}

/// <summary>0099: épico com a descrição e os cards (<c>GET Epics/{number}</c>).</summary>
public class EpicResponse : EpicSummary
{
    public string? Description { get; set; }
    public List<EpicCardResponse> Cards { get; set; } = [];

    public static EpicResponse From(Epic epic, EpicCounts counts)
    {
        var r = Fill(new EpicResponse(), epic, counts);
        r.Description = epic.Description;
        r.Cards = epic.Cards.OrderByDescending(c => c.AddedAt).Select(EpicCardResponse.From).ToList();
        return r;
    }
}

/// <summary>0099: card do DevOps no épico.</summary>
public class EpicCardResponse
{
    public string Card { get; set; } = string.Empty;
    public string Role { get; set; } = EpicCardRole.Linked;
    public string? Title { get; set; }
    public string? WorkItemType { get; set; }
    public string AddedByName { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; }

    public static EpicCardResponse From(EpicCard c) => new()
    {
        Card = c.CardNumber,
        Role = c.Role,
        Title = c.Title,
        WorkItemType = c.WorkItemType,
        AddedByName = c.AddedByName,
        AddedAt = c.AddedAt
    };
}
