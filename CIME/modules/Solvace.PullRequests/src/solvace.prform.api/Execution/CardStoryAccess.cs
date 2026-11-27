using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Responses;

namespace solvace.prform.Execution;

/// <summary>
/// 0104: o que o Especialista do caso de um CARD pode fazer nas histórias de um épico em que o card está. Ler: o card estar no
/// épico (qualquer papel). Escrever: o card estar como <c>created</c>/<c>linked</c> (só citado não basta) e o épico permitir
/// rascunhos pelos cards (<see cref="EpicSummary.AllowCardStoryDrafts"/>) — e só RASCUNHO, só os que o próprio card criou.
/// Converter em card: só quem tem o papel da Mesa (<see cref="ConvertDenied"/>), em qualquer token.
/// </summary>
public sealed class CardStoryAccess
{
    public required string Card { get; init; }
    public required long Epic { get; init; }
    public bool InEpic { get; init; }
    /// <summary>Card no épico como <c>created</c> ou <c>linked</c>.</summary>
    public bool Writer { get; init; }
    public bool Allowed { get; init; }

    public static CardStoryAccess Of(EpicResponse epic, string card)
    {
        var c = (card ?? string.Empty).Trim().TrimStart('#');
        var role = epic.Cards.FirstOrDefault(x => x.Card == c)?.Role;
        return new CardStoryAccess
        {
            Card = c,
            Epic = epic.Number,
            InEpic = role is not null,
            Writer = role is EpicCardRole.Created or EpicCardRole.Linked,
            Allowed = epic.AllowCardStoryDrafts
        };
    }

    public string NotWriter =>
        $"O card {Card} so foi citado no epico #{Epic} — para escrever historias aqui ele precisa estar no epico (criado ou vinculado). " +
        "Peca a quem cuida do epico na Mesa de gestao para vincular o card.";

    /// <summary>Motivo para não escrever (null = pode escrever rascunho).</summary>
    public string? WriteDenied =>
        !Writer ? NotWriter
        : !Allowed ? $"O epico #{Epic} nao permite rascunhos de historias pelos cards (opcao desligada na Mesa de gestao). " +
                     "Peca a quem cuida do epico para ligar a opcao ou escrever a historia pela Mesa."
        : null;

    /// <summary>Nota curta para a listagem (o especialista sabe de antemão se pode escrever).</summary>
    public string Note => WriteDenied ?? $"O card {Card} pode criar e editar rascunhos (draft) neste epico; converter em card e pela Mesa de gestao.";

    /// <summary>Pelo card só se escreve rascunho: <c>ready</c>/<c>discarded</c> recusados.</summary>
    public static string? StatusDenied(string? status) =>
        string.IsNullOrWhiteSpace(status) || string.Equals(status.Trim(), EpicStoryStatus.Draft, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"Pelo card a historia fica sempre como rascunho (draft) — '{status.Trim()}' e a conversao sao da Mesa de gestao.";

    /// <summary>Editar pelo card: só rascunho criado pelo próprio card.</summary>
    public static string? EditDenied(EpicStoryResponse story, string card)
    {
        if (!string.Equals(story.SourceCard, card, StringComparison.Ordinal))
            return $"A historia #{story.Number} nao foi criada pelo card {card} — pelo card so se editam os rascunhos que ele criou.";
        return story.Status == EpicStoryStatus.Draft
            ? null
            : $"A historia #{story.Number} nao e mais rascunho ({story.Status}) — a partir daqui ela e da Mesa de gestao.";
    }

    /// <summary>Converter em card: quem pediu precisa do papel admin/management (acesso à Mesa de gestão).</summary>
    public static string? ConvertDenied(IReadOnlyList<string> roles, string? who) =>
        DeskRoles.HasDeskAccess(roles)
            ? null
            : $"Converter a historia em card e da Mesa de gestao (papel admin ou management) — {(string.IsNullOrWhiteSpace(who) ? "quem pediu" : who.Trim())} " +
              "nao tem esse papel. O rascunho fica no epico; peca a quem cuida do epico para converter.";
}
