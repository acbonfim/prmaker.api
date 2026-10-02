namespace solvace.prform.Home;

/// <summary>Listas da home (0051): <c>mine</c> = cards registrados pelo usuário; <c>participated</c> = cards em que ele mexeu.</summary>
public static class HomeCardsScope
{
    public const string Mine = "mine";
    public const string Participated = "participated";
}

/// <summary>Como o usuário participou do card (selos "por que este card está aqui"). Mantenha em sincronia com o front.</summary>
public static class HomeCardRole
{
    public const string Register = "register";
    public const string PullRequest = "pr";
    public const string Timeline = "timeline";
    public const string Plan = "plan";
    public const string Handover = "handover";
}

/// <summary>Tipo da última atividade do card. Mantenha em sincronia com o front.</summary>
public static class HomeCardActivityKind
{
    public const string Register = "register";
    public const string PullRequest = "pr";
    public const string Timeline = "timeline";
    public const string Note = "note";
    public const string Plan = "plan";
    public const string Handover = "handover";
}

public sealed class HomeCardResponse
{
    public string CardNumber { get; init; } = string.Empty;
    /// <summary>Repositório do PR mais recente (para abrir a tela já com ele selecionado).</summary>
    public string? RepositoryId { get; init; }
    /// <summary>Quem registrou o card no PRMake (externalId). Nulo quando o card só tem plano/Timeline.</summary>
    public Guid? OwnerUserId { get; init; }
    public bool IsMine { get; init; }
    public bool Registered { get; init; }
    public bool HasDescription { get; init; }
    public bool HasRootCause { get; init; }
    public bool SummaryPublished { get; init; }
    /// <summary>Como o usuário atual participou (<see cref="HomeCardRole"/>).</summary>
    public IReadOnlyList<string> MyRoles { get; init; } = [];
    /// <summary>Última interação do usuário atual com o card (ordem da lista "participei").</summary>
    public DateTimeOffset? MyLastActivityAt { get; init; }
    public HomeCardActivity? LastActivity { get; init; }
    /// <summary>Envolvidos: dono primeiro, depois do mais recente para o mais antigo (no máximo <see cref="HomeCardsService.MaxParticipants"/>).</summary>
    public IReadOnlyList<HomeCardParticipant> Participants { get; init; } = [];
    public int ParticipantsCount { get; init; }
    public IReadOnlyList<HomeCardPullRequest> PullRequests { get; init; } = [];
    public HomeCardPlan? Plan { get; init; }
    public HomeCardTimeline Timeline { get; init; } = new();
    public HomeCardNotes Notes { get; init; } = new();
    public HomeCardHandover? Handover { get; init; }
}

public sealed record HomeCardParticipant(Guid? UserId, string Name, bool IsOwner);

public sealed record HomeCardActivity(string Kind, string Text, Guid? UserId, string? UserName, DateTimeOffset At);

public sealed record HomeCardPullRequest(string RepositoryId, int? Number, string Url, string Title, string Status,
    bool IsDraft, Guid UserId, DateTimeOffset CreatedAt);

public sealed class HomeCardPlan
{
    public Guid Id { get; init; }
    public string Phase { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int StepsDone { get; init; }
    public int StepsTotal { get; init; }
    /// <summary>Etapa em andamento (ou a que espera algo), se houver.</summary>
    public string? CurrentStep { get; init; }
    public string? CurrentStepStatus { get; init; }
    public string? WaitingOn { get; init; }
    public string? WaitingReason { get; init; }
    public int OpenQuestions { get; init; }
    /// <summary>Fases que o card tem (análise e/ou correção).</summary>
    public IReadOnlyList<string> Phases { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class HomeCardTimeline
{
    public int Count { get; init; }
    public HomeCardEntry? Last { get; init; }
}

public sealed class HomeCardNotes
{
    public int Count { get; init; }
    public HomeCardEntry? Last { get; init; }
}

/// <summary>Trecho de uma entrada da Timeline ou comentário do plano (texto sem markdown, cortado).</summary>
public sealed record HomeCardEntry(Guid? UserId, string UserName, string Excerpt, DateTimeOffset CreatedAt, int? Number = null);

public sealed record HomeCardHandover(Guid? UserId, DateTimeOffset At);
