using solvace.executionplans.domain.Entities;

namespace solvace.executionplans.application.Contracts;

/// <summary>0080: conversa do caso (Especialista do caso) — mesmo banco/contexto do plano.</summary>
public interface ICaseChatRepository
{
    /// <summary>Conversa do card, rastreada (null = ninguém escreveu ainda).</summary>
    Task<CaseChat?> GetChatAsync(string cardNumber, CancellationToken cancellationToken);
    void AddChat(CaseChat chat);

    Task<int> GetMaxMessageNumberAsync(string cardNumber, CancellationToken cancellationToken);
    void AddMessage(CaseMessage message);
    /// <summary>Mensagem rastreada.</summary>
    Task<CaseMessage?> GetMessageAsync(string cardNumber, Guid id, CancellationToken cancellationToken);
    Task<CaseMessage?> GetMessageByNumberAsync(string cardNumber, int number, CancellationToken cancellationToken);
    /// <summary>Mensagens (sem as apagadas), em ordem crescente; <paramref name="afterNumber"/> exclusivo; as últimas <paramref name="limit"/>.</summary>
    Task<List<CaseMessage>> GetMessagesAsync(string cardNumber, int? afterNumber, int limit, CancellationToken cancellationToken);
    /// <summary>0090 (<c>/clear</c>): marca como apagadas todas as mensagens até <paramref name="lastNumber"/> (em massa).</summary>
    Task<int> DeleteMessagesUpToAsync(string cardNumber, int lastNumber, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Mensagens escritas por um pedido do executor (rastreadas).</summary>
    Task<List<CaseMessage>> GetMessagesByRequestAsync(Guid requestId, CancellationToken cancellationToken);
    /// <summary>Pessoas que já escreveram na conversa (id, nome, quantas mensagens).</summary>
    Task<List<CaseParticipant>> GetParticipantsAsync(string cardNumber, CancellationToken cancellationToken);

    Task<int> GetMaxFileNumberAsync(string cardNumber, CancellationToken cancellationToken);
    Task<long> GetFilesSizeAsync(string cardNumber, CancellationToken cancellationToken);
    void AddFile(CaseFile file);
    /// <summary>Arquivos (sem o conteúdo) do card; com <paramref name="messageIds"/>, só desses.</summary>
    Task<List<CaseFile>> GetFilesAsync(string cardNumber, IReadOnlyCollection<Guid>? messageIds, CancellationToken cancellationToken);
    Task<CaseFile?> GetFileAsync(string cardNumber, Guid id, CancellationToken cancellationToken);
    Task<CaseFile?> GetFileByNumberAsync(string cardNumber, int number, CancellationToken cancellationToken);
    Task<byte[]?> GetFileContentAsync(Guid fileId, CancellationToken cancellationToken);
    /// <summary>Põe na mensagem os arquivos ainda soltos (gerados pelo especialista antes da resposta).</summary>
    Task AttachFilesAsync(IReadOnlyCollection<Guid> fileIds, Guid messageId, CancellationToken cancellationToken);

    Task<List<CasePoint>> GetPointsAsync(string cardNumber, CancellationToken cancellationToken);
    Task<CasePoint?> GetPointAsync(string cardNumber, Guid id, CancellationToken cancellationToken);
    void AddPoint(CasePoint point);

    Task<int> GetMaxProposalNumberAsync(string cardNumber, CancellationToken cancellationToken);
    /// <summary>Propostas do card (rastreadas), da mais antiga para a mais nova.</summary>
    Task<List<CaseProposal>> GetProposalsAsync(string cardNumber, CancellationToken cancellationToken);
    Task<CaseProposal?> GetProposalAsync(string cardNumber, Guid id, CancellationToken cancellationToken);
    void AddProposal(CaseProposal proposal);

    Task<int> GetMaxActionNumberAsync(string cardNumber, CancellationToken cancellationToken);
    Task<List<CaseAction>> GetActionsAsync(string cardNumber, CancellationToken cancellationToken);
    Task<CaseAction?> GetActionAsync(string cardNumber, Guid id, CancellationToken cancellationToken);
    void AddAction(CaseAction action);

    /// <summary>Salva. Número repetido (duas mensagens ao mesmo tempo) → <see cref="CaseNumberConflictException"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
    void ClearTracking();
}

public record CaseParticipant(Guid? UserId, string Name, int Messages, DateTimeOffset LastAt);

/// <summary>Duas gravações pegaram o mesmo número por card ao mesmo tempo — basta tentar de novo.</summary>
public class CaseNumberConflictException(Exception inner) : Exception("Número repetido na conversa do caso.", inner);
