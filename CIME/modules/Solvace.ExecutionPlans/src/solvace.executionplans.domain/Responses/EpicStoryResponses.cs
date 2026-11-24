using System.Text.Json;
using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.domain.Responses;

/// <summary>0099 (ajustes 2): história/bug do épico (JSON camelCase). <c>epicKey</c> = <c>E{epicNumber}</c>.</summary>
public class EpicStoryResponse
{
    public Guid Id { get; set; }
    public long Number { get; set; }
    public long EpicNumber { get; set; }
    public string EpicKey { get; set; } = string.Empty;
    public string Type { get; set; } = EpicStoryType.UserStory;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public string? ReproSteps { get; set; }
    public int? Priority { get; set; }
    public string? Severity { get; set; }
    public List<string> Tags { get; set; } = [];
    public Dictionary<string, JsonElement> Fields { get; set; } = new();
    public string Status { get; set; } = EpicStoryStatus.Draft;
    /// <summary>Card do DevOps criado pela conversão (null = ainda não convertida).</summary>
    public string? CardId { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }
    public string? ConvertedByName { get; set; }
    /// <summary>Conversão em andamento (a tela desliga o botão).</summary>
    public bool Converting { get; set; }
    /// <summary>Pode editar/descartar (não convertida nem em conversão).</summary>
    public bool Editable { get; set; }
    public Guid? CreatedBy { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static EpicStoryResponse From(EpicStory s, DateTimeOffset now)
    {
        var converting = s.IsConverting(now);
        return new EpicStoryResponse
        {
            Id = s.Id,
            Number = s.Number,
            EpicNumber = s.EpicNumber,
            EpicKey = Entities.EpicKey.Of(s.EpicNumber),
            Type = s.Type,
            Title = s.Title,
            Description = s.Description,
            AcceptanceCriteria = s.AcceptanceCriteria,
            ReproSteps = s.ReproSteps,
            Priority = s.Priority,
            Severity = s.Severity,
            Tags = s.Tags.ToList(),
            Fields = s.Fields.ToDictionary(f => f.Key, f => f.Value),
            Status = s.Status,
            CardId = s.CardId,
            ConvertedAt = s.ConvertedAt,
            ConvertedByName = s.ConvertedByName,
            Converting = converting,
            Editable = !s.IsConverted && !converting,
            CreatedBy = s.CreatedBy,
            CreatedByName = s.CreatedByName,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt
        };
    }
}
