using System.Text.RegularExpressions;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0080: Especialista do caso — a conversa do card (não do plano: atravessa análise, correção e versões) entre as
/// pessoas que tratam o card e um agente próprio, que roda numa pista separada do executor (pedido
/// <see cref="ExecutionRequestKind.Specialist"/>) e ajusta o plano atual só a pedido (0096). Aqui ficam a sessão do Claude do especialista
/// (retomada a cada mensagem) e até onde ele leu.
/// </summary>
public class CaseChat
{
    public string CardNumber { get; private set; } = string.Empty;
    public string? SessionId { get; private set; }
    public string? SessionHost { get; private set; }
    public string? SessionCwd { get; private set; }
    /// <summary>Última mensagem que o especialista leu (o próximo pedido começa dali).</summary>
    public int LastReadNumber { get; private set; }
    /// <summary>Chegou mensagem para o especialista enquanto ele rodava: ao terminar, roda de novo.</summary>
    public bool PendingRun { get; private set; }
    public long InputTokens { get; private set; }
    public long OutputTokens { get; private set; }
    public decimal CostUsd { get; private set; }
    /// <summary>0090: última vez que alguém limpou a conversa (<c>/clear</c>) — o que veio antes sai da tela e do contexto.</summary>
    public DateTimeOffset? ClearedAt { get; private set; }
    public string? ClearedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    protected CaseChat() { }

    public CaseChat(string cardNumber, DateTimeOffset now)
    {
        CardNumber = NormalizeCard(cardNumber);
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static string NormalizeCard(string? card)
    {
        var c = (card ?? string.Empty).Trim().TrimStart('#');
        if (c.Length is 0 or > 100)
            throw new DomainException("Card inválido.");
        return c;
    }

    /// <param name="requestedAt">Quando o pedido do executor foi criado: sessão de pedido anterior à limpeza não volta.</param>
    public void RegisterSession(string? sessionId, string? host, string? cwd, DateTimeOffset now, DateTimeOffset? requestedAt = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        if (ClearedAt is { } cleared && requestedAt is { } at && at < cleared) return;
        SessionId = Cut(sessionId.Trim(), ExecutionSession.MaxSessionIdLength);
        SessionHost = host is null ? SessionHost : Cut(host, ExecutionSession.MaxHostLength);
        SessionCwd = cwd is null ? SessionCwd : Cut(cwd, ExecutionSession.MaxCwdLength);
        UpdatedAt = now;
    }

    public void MarkRead(int number, DateTimeOffset now)
    {
        if (number <= LastReadNumber) return;
        LastReadNumber = number;
        UpdatedAt = now;
    }

    public void SetPendingRun(bool pending, DateTimeOffset now)
    {
        PendingRun = pending;
        UpdatedAt = now;
    }

    /// <summary>
    /// 0090 (<c>/clear</c>): a conversa recomeça — a sessão do Claude do especialista é esquecida (a próxima resposta abre
    /// outra, só com o contexto do caso) e ele não tem nada a ler até <paramref name="lastNumber"/>.
    /// </summary>
    public void Clear(string by, int lastNumber, DateTimeOffset now)
    {
        var who = (by ?? string.Empty).Trim();
        ClearedBy = who.Length == 0 ? "Usuário" : who.Length <= 200 ? who : who[..200];
        ClearedAt = now;
        SessionId = null;
        SessionHost = null;
        SessionCwd = null;
        PendingRun = false;
        if (lastNumber > LastReadNumber) LastReadNumber = lastNumber;
        UpdatedAt = now;
    }

    /// <summary>Criado depois da última limpeza (ou não houve limpeza) — continua na tela e no contexto.</summary>
    public bool Shows(DateTimeOffset createdAt) => ClearedAt is not { } cleared || createdAt > cleared;

    public void AddUsage(long? input, long? output, decimal? cost, DateTimeOffset now)
    {
        InputTokens += Math.Max(0, input ?? 0);
        OutputTokens += Math.Max(0, output ?? 0);
        CostUsd += Math.Max(0, cost ?? 0);
        UpdatedAt = now;
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>
/// Confirmação de leitura: até que mensagem cada pessoa já viu a conversa (o ✓✓ de quem escreveu). Gravada por
/// upsert que só avança (<c>GREATEST</c>) — nunca disputa a versão de <see cref="CaseChat"/> com o executor.
/// </summary>
public class CaseReader
{
    public const int MaxNameLength = 200;

    public string CardNumber { get; private set; } = string.Empty;
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int LastSeenNumber { get; private set; }
    public DateTimeOffset SeenAt { get; private set; }

    protected CaseReader() { }

    public CaseReader(string cardNumber, Guid userId, string? name, int lastSeenNumber, DateTimeOffset seenAt)
    {
        CardNumber = CaseChat.NormalizeCard(cardNumber);
        UserId = userId;
        var n = (name ?? string.Empty).Trim();
        Name = n.Length == 0 ? "Usuário" : n.Length <= MaxNameLength ? n : n[..MaxNameLength];
        LastSeenNumber = Math.Max(0, lastSeenNumber);
        SeenAt = seenAt;
    }
}

public static class CaseAuthorKind
{
    public const string Person = "person";
    public const string Specialist = "specialist";
    public const string System = "system";
}

public static class CaseMessageKind
{
    public const string Text = "text";
    public const string Voice = "voice";
    public const string Proposal = "proposal";
    public const string Action = "action";
    public const string System = "system";
}

/// <summary>Mensagem da conversa do caso (número por card, citável: "mensagem 7").</summary>
public partial class CaseMessage
{
    public const int MaxTextLength = 40_000;
    public const int MaxTranscriptLength = 8_000;
    public const int MaxSteps = 20;
    public const int MaxStepLength = 300;
    public const int MaxMentions = 20;
    public const int MaxAttachments = 20;

    public Guid Id { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public int Number { get; private set; }
    public string AuthorKind { get; private set; } = CaseAuthorKind.Person;
    public Guid? AuthorUserId { get; private set; }
    public string AuthorName { get; private set; } = string.Empty;
    public string Kind { get; private set; } = CaseMessageKind.Text;
    public string Text { get; private set; } = string.Empty;
    /// <summary>Mensagem de voz: o que o navegador transcreveu (e a pessoa revisou) — é o que o especialista lê.</summary>
    public string? Transcript { get; private set; }
    /// <summary>"Só entre pessoas": o especialista não responde a esta.</summary>
    public bool PeopleOnly { get; private set; }
    /// <summary>"Investigar a fundo": o especialista roda com o modelo de investigação.</summary>
    public bool Deep { get; private set; }
    public List<string> Mentions { get; private set; } = [];
    /// <summary>O que o especialista fez para responder ("Li o print", "Consultei ... (só leitura)").</summary>
    public List<string> Steps { get; private set; } = [];
    public int? ReplyToNumber { get; private set; }
    public Guid? ProposalId { get; private set; }
    public Guid? ActionId { get; private set; }
    /// <summary>Pedido do executor que escreveu (especialista): o consumo dele vem no fim.</summary>
    public Guid? RequestId { get; private set; }
    public string? Model { get; private set; }
    public long? InputTokens { get; private set; }
    public long? OutputTokens { get; private set; }
    public int? DurationMs { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? EditedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    protected CaseMessage() { }

    public CaseMessage(string cardNumber, int number, string authorKind, Guid? authorUserId, string authorName, string kind,
        string? text, bool hasAttachments, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CardNumber = CaseChat.NormalizeCard(cardNumber);
        Number = number;
        AuthorKind = authorKind is CaseAuthorKind.Person or CaseAuthorKind.Specialist or CaseAuthorKind.System
            ? authorKind : throw new DomainException($"Autor inválido: '{authorKind}'.");
        AuthorUserId = authorUserId;
        AuthorName = Cut(string.IsNullOrWhiteSpace(authorName) ? "Usuário" : authorName.Trim(), 200);
        Kind = kind is CaseMessageKind.Text or CaseMessageKind.Voice or CaseMessageKind.Proposal or CaseMessageKind.Action or CaseMessageKind.System
            ? kind : throw new DomainException($"Tipo de mensagem inválido: '{kind}'.");
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0 && !hasAttachments && kind != CaseMessageKind.Voice)
            throw new DomainException("Escreva uma mensagem, grave um áudio ou anexe um arquivo.");
        if (t.Length > MaxTextLength)
            throw new DomainException($"A mensagem pode ter no máximo {MaxTextLength} caracteres.");
        Text = t;
        Mentions = ExtractMentions(t);
        CreatedAt = now;
    }

    public bool IsDeleted => DeletedAt.HasValue;
    public bool FromSpecialist => AuthorKind == CaseAuthorKind.Specialist;
    public bool FromPerson => AuthorKind == CaseAuthorKind.Person;

    public void SetVoice(string? transcript)
    {
        var t = (transcript ?? string.Empty).Trim();
        Transcript = t.Length == 0 ? null : Cut(t, MaxTranscriptLength);
    }

    public void SetFlags(bool peopleOnly, bool deep)
    {
        PeopleOnly = peopleOnly;
        Deep = deep;
    }

    public void SetReplyTo(int? number) => ReplyToNumber = number is > 0 ? number : null;

    public void SetSteps(IEnumerable<string>? steps) =>
        Steps = (steps ?? [])
            .Select(s => (s ?? string.Empty).Trim())
            .Where(s => s.Length > 0)
            .Select(s => Cut(s, MaxStepLength))
            .Take(MaxSteps)
            .ToList();

    public void LinkProposal(Guid id) => ProposalId = id;
    public void LinkAction(Guid id) => ActionId = id;

    public void SetRequest(Guid? requestId, string? model)
    {
        RequestId = requestId;
        Model = model is null ? null : Cut(model, ExecutionModelUsage.MaxModelLength);
    }

    public void SetUsage(long? input, long? output, int? durationMs, string? model)
    {
        InputTokens = input;
        OutputTokens = output;
        DurationMs = durationMs;
        if (!string.IsNullOrWhiteSpace(model))
            Model = Cut(model, ExecutionModelUsage.MaxModelLength);
    }

    public void Edit(string? text, DateTimeOffset now)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0)
            throw new DomainException("Escreva a mensagem.");
        if (t.Length > MaxTextLength)
            throw new DomainException($"A mensagem pode ter no máximo {MaxTextLength} caracteres.");
        Text = t;
        Mentions = ExtractMentions(t);
        EditedAt = now;
    }

    public void Delete(DateTimeOffset now) => DeletedAt = now;

    /// <summary>Só a pessoa que escreveu mexe na mensagem dela; mensagens do especialista e do sistema ficam.</summary>
    public bool CanBeChangedBy(Guid? userId) => FromPerson && userId.HasValue && userId == AuthorUserId;

    /// <summary>
    /// Decisão 2 da 0080: com uma pessoa só na conversa o especialista responde sempre; com duas ou mais, só quando for
    /// mencionado ou quando a mensagem for uma pergunta. "Só entre pessoas" nunca.
    /// </summary>
    public bool AsksSpecialist(int peopleInConversation)
    {
        if (!FromPerson || PeopleOnly) return false;
        if (peopleInConversation <= 1) return true;
        if (MentionsSpecialist) return true;
        var content = Kind == CaseMessageKind.Voice ? Transcript ?? string.Empty : Text;
        return content.Contains('?');
    }

    public bool MentionsSpecialist => Mentions.Any(IsSpecialistMention);

    public static bool IsSpecialistMention(string mention) =>
        mention.StartsWith("especialista", StringComparison.OrdinalIgnoreCase)
        || mention.Equals("claude", StringComparison.OrdinalIgnoreCase);

    public static List<string> ExtractMentions(string text) =>
        MentionRegex().Matches(text)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxMentions)
            .ToList();

    [GeneratedRegex(@"(?<![\w@])@([\p{L}][\p{L}\p{N}._-]{1,60})")]
    private static partial Regex MentionRegex();

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Arquivo da conversa (imagem colada, áudio, documento, imagem/arquivo gerado pelo especialista).</summary>
public class CaseFile
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const long MaxCardBytes = 100L * 1024 * 1024;
    public const int MaxNameLength = 200;

    public Guid Id { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public Guid? MessageId { get; private set; }
    public int Number { get; private set; }
    public string Name { get; private set; } = string.Empty;
    /// <summary>image | audio | file.</summary>
    public string Kind { get; private set; } = "file";
    public string ContentType { get; private set; } = "application/octet-stream";
    public long Size { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public bool FromSpecialist { get; private set; }
    /// <summary>
    /// 0085 r5 (1.7): áudio enviado por uma pessoa (mensagem de voz/ditado) — serve para a transcrição e toca dentro da
    /// mensagem, mas não é "arquivo da conversa": fica fora da lista de arquivos e do contexto do especialista.
    /// </summary>
    public bool Hidden { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public CaseFileContent? Content { get; private set; }

    protected CaseFile() { }

    public CaseFile(string cardNumber, Guid? messageId, int number, string name, string? contentType, byte[] data, string createdBy,
        bool fromSpecialist, DateTimeOffset now)
    {
        if (data.LongLength == 0)
            throw new DomainException($"O arquivo '{name}' está vazio.");
        if (data.LongLength > MaxFileBytes)
            throw new DomainException($"O arquivo '{name}' passa do limite de {MaxFileBytes / (1024 * 1024)} MB.");
        Id = Guid.NewGuid();
        CardNumber = CaseChat.NormalizeCard(cardNumber);
        MessageId = messageId;
        Number = number;
        Name = ExecutionArtifact.NormalizeName(name);
        if (Name.Length > MaxNameLength) Name = Name[^MaxNameLength..];
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim().ToLowerInvariant();
        if (ContentType.Length > 150) ContentType = ContentType[..150];
        Kind = KindOf(ContentType, Name);
        Size = data.LongLength;
        CreatedBy = createdBy.Length <= 200 ? createdBy : createdBy[..200];
        FromSpecialist = fromSpecialist;
        Hidden = IsPersonAudio(Kind, fromSpecialist);
        CreatedAt = now;
        Content = new CaseFileContent(Id, data);
    }

    /// <summary>0085 r5 (1.7): áudio de quem conversa não vira arquivo da conversa (o do especialista, se houver, vira).</summary>
    public static bool IsPersonAudio(string kind, bool fromSpecialist) => !fromSpecialist && kind == "audio";

    /// <summary>0085 r5 (1.7): só os arquivos que aparecem como "arquivos da conversa" (lista, contexto, mensagens para o especialista).</summary>
    public static IEnumerable<CaseFile> Listed(IEnumerable<CaseFile> files) => files.Where(f => !f.Hidden);

    /// <summary>Arquivo gerado pelo especialista antes da resposta: entra na mensagem dela.</summary>
    public void AttachTo(Guid messageId)
    {
        if (MessageId is not null && MessageId != messageId)
            throw new DomainException($"O arquivo #{Number} já está em outra mensagem.");
        MessageId = messageId;
    }

    public static string KindOf(string contentType, string name)
    {
        if (contentType.StartsWith("image/")) return "image";
        if (contentType.StartsWith("audio/")) return "audio";
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => "image",
            ".webm" or ".ogg" or ".mp3" or ".m4a" or ".wav" => "audio",
            _ => "file"
        };
    }
}

public class CaseFileContent
{
    public Guid FileId { get; private set; }
    public byte[] Data { get; private set; } = [];

    protected CaseFileContent() { }

    public CaseFileContent(Guid fileId, byte[] data)
    {
        FileId = fileId;
        Data = data;
    }
}

public static class CasePointStatus
{
    public const string Agreed = "agreed";
    public const string Discussing = "discussing";
    public const string Dropped = "dropped";

    public static string Normalize(string? status) => (status ?? Agreed).Trim().ToLowerInvariant() switch
    {
        Agreed or "ok" or "combinado" => Agreed,
        Discussing or "open" or "em discussao" or "em discussão" => Discussing,
        Dropped or "descartado" => Dropped,
        var s => throw new DomainException($"Status de ponto inválido: '{s}'.")
    };
}

/// <summary>Ponto combinado na conversa ("Base = qa, sem mexer em extensões") — a base das propostas.</summary>
public class CasePoint
{
    public const int MaxTextLength = 500;

    public Guid Id { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public string Status { get; private set; } = CasePointStatus.Agreed;
    public int? MessageNumber { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public bool FromSpecialist { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    protected CasePoint() { }

    public CasePoint(string cardNumber, string text, string? status, int? messageNumber, string createdBy, bool fromSpecialist, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CardNumber = CaseChat.NormalizeCard(cardNumber);
        SetText(text);
        Status = CasePointStatus.Normalize(status);
        MessageNumber = messageNumber is > 0 ? messageNumber : null;
        CreatedBy = createdBy.Length <= 200 ? createdBy : createdBy[..200];
        FromSpecialist = fromSpecialist;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public void Update(string? text, string? status, DateTimeOffset now)
    {
        if (text is not null) SetText(text);
        if (status is not null) Status = CasePointStatus.Normalize(status);
        UpdatedAt = now;
    }

    private void SetText(string text)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) throw new DomainException("Escreva o ponto combinado.");
        Text = t.Length <= MaxTextLength ? t : t[..MaxTextLength];
    }
}

public static class CaseProposalStatus
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    /// <summary>Outra proposta mais nova tomou o lugar desta.</summary>
    public const string Superseded = "superseded";
}

/// <summary>Uma mudança da proposta (etapa nova, alterada, removida, decisão que muda), com a mensagem de origem.</summary>
public class CaseProposalChange
{
    public const int MaxTitleLength = 300;
    public const int MaxDetailLength = 1_000;

    /// <summary>add | change | remove | decision | keep.</summary>
    public string Kind { get; set; } = "change";
    public string Title { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public int? FromMessage { get; set; }

    public static readonly IReadOnlySet<string> Kinds = new HashSet<string> { "add", "change", "remove", "decision", "keep" };
}

/// <summary>
/// Proposta do especialista para mudar a solução do plano. Nada muda no plano até alguém com permissão aceitar; aceita,
/// vira uma instrução no plano atual (card 78344: o executor principal ajusta esse mesmo plano — sem versão nova).
/// </summary>
public class CaseProposal
{
    public const int MaxTitleLength = 300;
    public const int MaxSummaryLength = 8_000;
    public const int MaxChanges = 30;

    public Guid Id { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public int Number { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Summary { get; private set; }
    /// <summary>Fase da nova versão: analysis | correction.</summary>
    public string Phase { get; private set; } = ExecutionPhase.Correction;
    public List<CaseProposalChange> Changes { get; private set; } = [];
    public string Status { get; private set; } = CaseProposalStatus.Pending;
    /// <summary>Aceita para já executar (senão só monta a nova versão e espera).</summary>
    public bool Execute { get; private set; }
    public string? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? RejectReason { get; private set; }
    /// <summary>Comentário-instrução gravado no plano ao aceitar.</summary>
    public int? PlanNoteNumber { get; private set; }
    public Guid? MessageId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    protected CaseProposal() { }

    public CaseProposal(string cardNumber, int number, string title, string? summary, string? phase, IEnumerable<CaseProposalChange> changes, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CardNumber = CaseChat.NormalizeCard(cardNumber);
        Number = number;
        var t = (title ?? string.Empty).Trim();
        if (t.Length == 0) throw new DomainException("Dê um título à proposta.");
        Title = t.Length <= MaxTitleLength ? t : t[..MaxTitleLength];
        var s = summary?.Trim();
        Summary = string.IsNullOrEmpty(s) ? null : s.Length <= MaxSummaryLength ? s : s[..MaxSummaryLength];
        Phase = (phase ?? ExecutionPhase.Correction).Trim().ToLowerInvariant() switch
        {
            ExecutionPhase.Analysis => ExecutionPhase.Analysis,
            ExecutionPhase.Correction => ExecutionPhase.Correction,
            var p => throw new DomainException($"Fase inválida: '{p}' (analysis|correction).")
        };
        Changes = (changes ?? []).Select(NormalizeChange).Take(MaxChanges).ToList();
        if (Changes.Count == 0)
            throw new DomainException("A proposta precisa dizer o que muda.");
        CreatedAt = now;
    }

    public bool IsPending => Status == CaseProposalStatus.Pending;

    public void LinkMessage(Guid messageId) => MessageId = messageId;

    public void Accept(bool execute, string by, DateTimeOffset now)
    {
        if (!IsPending) throw new DomainException("Esta proposta já foi decidida.");
        Status = CaseProposalStatus.Accepted;
        Execute = execute;
        DecidedBy = by.Length <= 200 ? by : by[..200];
        DecidedAt = now;
    }

    public void SetPlanNote(int number) => PlanNoteNumber = number;

    public void Reject(string? reason, string by, DateTimeOffset now)
    {
        if (!IsPending) throw new DomainException("Esta proposta já foi decidida.");
        Status = CaseProposalStatus.Rejected;
        var r = reason?.Trim();
        RejectReason = string.IsNullOrEmpty(r) ? null : r.Length <= 500 ? r : r[..500];
        DecidedBy = by.Length <= 200 ? by : by[..200];
        DecidedAt = now;
    }

    public void Supersede(DateTimeOffset now)
    {
        if (!IsPending) return;
        Status = CaseProposalStatus.Superseded;
        DecidedAt = now;
    }

    private static CaseProposalChange NormalizeChange(CaseProposalChange c)
    {
        var kind = (c.Kind ?? "change").Trim().ToLowerInvariant();
        if (!CaseProposalChange.Kinds.Contains(kind))
            throw new DomainException($"Tipo de mudança inválido: '{kind}' (add|change|remove|decision|keep).");
        var title = (c.Title ?? string.Empty).Trim();
        if (title.Length == 0) throw new DomainException("Cada mudança precisa de um título.");
        var detail = c.Detail?.Trim();
        return new CaseProposalChange
        {
            Kind = kind,
            Title = title.Length <= CaseProposalChange.MaxTitleLength ? title : title[..CaseProposalChange.MaxTitleLength],
            Detail = string.IsNullOrEmpty(detail) ? null : detail.Length <= CaseProposalChange.MaxDetailLength ? detail : detail[..CaseProposalChange.MaxDetailLength],
            FromMessage = c.FromMessage is > 0 ? c.FromMessage : null
        };
    }

    /// <summary>Texto da instrução que vai para o plano quando a proposta é aceita (lido pela analisar-bug/analisar-us).</summary>
    /// <param name="planKind">r5: Kind do plano (analisar-us → "plano de implementação").</param>
    public string InstructionText(string acceptedBy, string? planKind = null)
    {
        var lines = new List<string>
        {
            $"**Proposta do Especialista aceita** (#{Number}, por {acceptedBy}): {Title}",
            string.Empty,
            Phase == ExecutionPhase.Correction
                ? $"Ajuste o {PlanTerms.DeliveryPlan(planKind)} ATUAL, no lugar (prmake_steps no mesmo plano — sem plano novo nem versão nova; etapas já feitas ficam), com estas mudanças:"
                : "Ajuste a análise atual, no lugar (mesmo plano, sem versão nova; refaça só o que mudou), com estas mudanças:"
        };
        foreach (var c in Changes)
        {
            var label = c.Kind switch
            {
                "add" => "Adicionar",
                "remove" => "Remover",
                "decision" => "Decisão",
                "keep" => "Manter",
                _ => "Alterar"
            };
            var from = c.FromMessage is { } n ? $" (mensagem {n} da conversa)" : string.Empty;
            lines.Add($"- **{label}:** {c.Title}{(c.Detail is null ? string.Empty : " — " + c.Detail)}{from}");
        }
        if (Summary is not null)
        {
            lines.Add(string.Empty);
            lines.Add(Summary);
        }
        lines.Add(string.Empty);
        lines.Add(Execute
            ? "Pode executar o plano ajustado em seguida (sem merge)."
            : $"Só ajuste as etapas e pare: NÃO execute a {PlanTerms.Delivery(planKind)} até a pessoa pedir.");
        return string.Join("\n", lines);
    }
}

public static class CaseActionStatus
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Rejected = "rejected";
    public const string Done = "done";
    public const string Failed = "failed";
}

/// <summary>
/// Ação com efeito fora da conversa que o especialista quer fazer (subir um arquivo na QA, abrir um chamado...): só roda
/// depois que uma pessoa confirma no cartão da conversa.
/// </summary>
public class CaseAction
{
    public const int MaxTitleLength = 300;
    public const int MaxDetailLength = 4_000;
    public const int MaxResultLength = 2_000;

    public Guid Id { get; private set; }
    public string CardNumber { get; private set; } = string.Empty;
    public int Number { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public string Status { get; private set; } = CaseActionStatus.Pending;
    public string? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? Result { get; private set; }
    public DateTimeOffset? DoneAt { get; private set; }
    public Guid? MessageId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    protected CaseAction() { }

    public CaseAction(string cardNumber, int number, string title, string? detail, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CardNumber = CaseChat.NormalizeCard(cardNumber);
        Number = number;
        var t = (title ?? string.Empty).Trim();
        if (t.Length == 0) throw new DomainException("Diga qual é a ação.");
        Title = t.Length <= MaxTitleLength ? t : t[..MaxTitleLength];
        var d = detail?.Trim();
        Detail = string.IsNullOrEmpty(d) ? null : d.Length <= MaxDetailLength ? d : d[..MaxDetailLength];
        CreatedAt = now;
    }

    public void LinkMessage(Guid messageId) => MessageId = messageId;

    public void Decide(bool confirm, string by, DateTimeOffset now)
    {
        if (Status != CaseActionStatus.Pending) throw new DomainException("Esta ação já foi decidida.");
        Status = confirm ? CaseActionStatus.Confirmed : CaseActionStatus.Rejected;
        DecidedBy = by.Length <= 200 ? by : by[..200];
        DecidedAt = now;
    }

    public void Finish(bool ok, string? result, DateTimeOffset now)
    {
        if (Status != CaseActionStatus.Confirmed) throw new DomainException("Só uma ação confirmada pode ser concluída.");
        Status = ok ? CaseActionStatus.Done : CaseActionStatus.Failed;
        var r = result?.Trim();
        Result = string.IsNullOrEmpty(r) ? null : r.Length <= MaxResultLength ? r : r[..MaxResultLength];
        DoneAt = now;
    }
}
