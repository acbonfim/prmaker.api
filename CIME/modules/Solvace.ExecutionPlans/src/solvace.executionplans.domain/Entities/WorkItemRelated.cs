namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0094: de onde veio a relação de um item relatado ao card. Compartilhado com o frontend (selo de origem) e com as
/// skills — mantenha em sincronia. <c>manual</c> nunca é rebaixado por uma origem automática.
/// </summary>
public static class WorkItemRelatedSource
{
    /// <summary>Mencionado (<c>#n</c>/<c>##n</c>) numa mensagem do Especialista do caso.</summary>
    public const string Specialist = "specialist";
    /// <summary>Mencionado numa nota/análise do plano ou no resultado de uma sub-tarefa.</summary>
    public const string Plan = "plan";
    /// <summary>Mencionado numa entrada da Timeline ou comentário.</summary>
    public const string Timeline = "timeline";
    /// <summary>Mencionado na descrição ou sub-tarefa de um item do card.</summary>
    public const string ItemRef = "item-ref";
    /// <summary>Adicionado à mão (busca por número/título).</summary>
    public const string Manual = "manual";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Specialist, Plan, Timeline, ItemRef, Manual };

    public static bool IsManual(string source) => source == Manual;
}

/// <summary>
/// 0094: item de trabalho (de outro card, em geral) relatado a um card — por menção (<c>#n</c>) numa conversa do
/// Especialista, no plano/Timeline/descrição de item, ou adicionado à mão. Aparece na aba "Relatado" (somente leitura)
/// e entra no contexto das análises de IA. A relação aponta para o número do item dono (<see cref="RelatedItemNumber"/>);
/// se a menção foi a uma sub-tarefa, <see cref="RelatedTaskNumber"/> guarda qual, para destacar.
/// </summary>
public class WorkItemRelated
{
    public const int MaxContextLength = 500;

    public Guid Id { get; private set; }
    /// <summary>Card dono da aba "Relatado".</summary>
    public string CardNumber { get; private set; } = string.Empty;
    /// <summary>Número do item relatado (item dono — a sub-tarefa citada vai em <see cref="RelatedTaskNumber"/>).</summary>
    public long RelatedItemNumber { get; private set; }
    public long? RelatedTaskNumber { get; private set; }
    public string Source { get; private set; } = WorkItemRelatedSource.Manual;
    public string? Context { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public string CreatedByName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    protected WorkItemRelated() { }

    public WorkItemRelated(string cardNumber, long relatedItemNumber, long? relatedTaskNumber, string source, string? context,
        Guid? createdBy, string createdByName, DateTimeOffset now)
    {
        var card = (cardNumber ?? string.Empty).Trim();
        if (card.Length == 0 || card.Length > 100)
            throw new DomainException("Informe o número do card.");
        if (relatedItemNumber <= 0)
            throw new DomainException("Número do item relatado inválido.");
        var s = (source ?? string.Empty).Trim().ToLowerInvariant();
        if (!WorkItemRelatedSource.All.Contains(s))
            throw new DomainException($"Origem de relatado inválida: '{source}'.");

        Id = Guid.NewGuid();
        CardNumber = card;
        RelatedItemNumber = relatedItemNumber;
        RelatedTaskNumber = relatedTaskNumber is > 0 ? relatedTaskNumber : null;
        Source = s;
        Context = WorkItem.Clean(context, MaxContextLength);
        CreatedBy = createdBy;
        CreatedByName = WorkItem.Clean(createdByName, 200) ?? "Usuário";
        CreatedAt = now;
    }

    /// <summary>
    /// Reafirma uma relação já existente (upsert): atualiza a origem/contexto e a sub-tarefa citada, mas **nunca rebaixa um
    /// relatado manual** para uma origem automática. Devolve se mudou algo.
    /// </summary>
    public bool Touch(string source, string? context, long? relatedTaskNumber)
    {
        var s = (source ?? string.Empty).Trim().ToLowerInvariant();
        if (!WorkItemRelatedSource.All.Contains(s)) return false;
        if (WorkItemRelatedSource.IsManual(Source) && !WorkItemRelatedSource.IsManual(s)) return false;

        var changed = false;
        if (Source != s) { Source = s; changed = true; }
        var ctx = WorkItem.Clean(context, MaxContextLength);
        if (!string.IsNullOrWhiteSpace(ctx) && ctx != Context) { Context = ctx; changed = true; }
        if (relatedTaskNumber is > 0 && relatedTaskNumber != RelatedTaskNumber) { RelatedTaskNumber = relatedTaskNumber; changed = true; }
        return changed;
    }
}
