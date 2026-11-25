namespace solvace.executionplans.domain.Entities;

/// <summary>0085: histórico do item de trabalho (toda transição do item ou de uma sub-tarefa).</summary>
public class WorkItemLog
{
    public const int MaxTextLength = 4_000;
    public const int MaxActorLength = 200;
    public const int MaxKindLength = 30;

    public long Id { get; private set; }
    public Guid WorkItemId { get; private set; }
    public long? TaskNumber { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string Kind { get; private set; } = WorkItemLogKind.Info;
    public string Text { get; private set; } = string.Empty;

    protected WorkItemLog() { }

    public WorkItemLog(Guid workItemId, long? taskNumber, string actor, string kind, string text, DateTimeOffset now)
    {
        WorkItemId = workItemId;
        TaskNumber = taskNumber is > 0 ? taskNumber : null;
        At = now;
        Actor = WorkItem.Clean(actor, MaxActorLength) ?? "Sistema";
        Kind = WorkItem.Clean(kind, MaxKindLength) ?? WorkItemLogKind.Info;
        Text = WorkItem.Clean(text, MaxTextLength) ?? "-";
    }
}
