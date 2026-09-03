namespace solvace.timeline.domain.RealTime;

/// <summary>
/// Contrato de tempo real da Timeline. Os nomes de grupo/evento são compartilhados com o
/// frontend — mantenha em sincronia com o WsService/CardTimelineComponent.
/// </summary>
public static class TimelineRealTimeEvents
{
    /// <summary>Evento emitido quando qualquer entrada da timeline de um card muda.</summary>
    public const string EventTimelineUpdated = "timelineUpdated";

    /// <summary>Grupo por card. Só quem está naquele card recebe as atualizações.</summary>
    public static string Group(string cardNumber) => $"timeline:{cardNumber}";

    /// <summary>
    /// Ação que originou o evento. O frontend usa para decidir o feedback visual:
    /// "create" => skeleton de comentário novo no fim; "update"/"delete" => skeleton no
    /// comentário específico (via EntryId). Mantenha em sincronia com o CardTimelineComponent.
    /// </summary>
    public static class Actions
    {
        public const string Create = "create";
        public const string Update = "update";
        public const string Delete = "delete";
    }
}
