using solvace.prform.domain.Entities;

namespace solvace.prform.domain.Requests;

/// <summary>
/// Salva o registro do card (um por card). Description/RootCause são opcionais:
/// null mantém o valor atual, string vazia limpa.
/// </summary>
public class PullRequestRegisterRequest
{
    public string? Description { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string? RootCause { get; set; }
    public int FormId { get; set; }
    public Guid UserId { get; set; }

    // Legado: aceitos e ignorados (branch/repositório agora pertencem a cada PR do GitHub).
    // Mantidos para não quebrar clientes antigos (ex.: skill gerar-prmake).
    public string? BranchPrefix { get; set; }
    public string? BranchName { get; set; }
    public string? RepositoryId { get; set; }

    public PullRequestRegister Create(PullRequestRegisterRequest request)
    {
        return new PullRequestRegister(request);
    }
}
