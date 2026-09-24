using Cime.BuildingBlocks.RealTime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using solvace.github.application.Contract;
using solvace.github.domain.Responses;
using solvace.prform.domain.Entities;
using solvace.prform.application;
using solvace.prform.domain.Enums;
using solvace.prform.domain.RealTime;
using solvace.prform.domain.Requests;
using solvace.prform.domain.Responses;
using solvace.prform.Infra.Contexts;

namespace solvace.github.application.Services;

public class PullRequestGithubApplication : IPullRequestGithubApplication
{
    // Único formulário existente; o front também envia formId = 1 ao salvar o card.
    private const int DefaultFormId = 1;

    private readonly DefaultContext _context;
    private readonly IGitHubService _gitHubService;
    private readonly ILogger<PullRequestGithubApplication> _logger;
    private readonly IRealTimeNotifier _realTimeNotifier;

    public PullRequestGithubApplication(DefaultContext context, IGitHubService gitHubService, ILogger<PullRequestGithubApplication> logger,
        IRealTimeNotifier realTimeNotifier)
    {
        _context = context;
        _gitHubService = gitHubService;
        _logger = logger;
        _realTimeNotifier = realTimeNotifier;
    }

    public async Task<PullRequestGithubResponse> Open(string cardNumber, OpenPullRequestGithubRequest request, CancellationToken cancellationToken)
    {
        cardNumber = NormalizeCard(cardNumber);
        if (string.IsNullOrWhiteSpace(request.RepositoryId) || string.IsNullOrWhiteSpace(request.BranchName)
            || string.IsNullOrWhiteSpace(request.TargetBranch) || string.IsNullOrWhiteSpace(request.Title))
            throw new DomainException("Repositório, branch, branch de destino e título são obrigatórios");

        var sourceBranch = $"{request.BranchPrefix?.Trim()}{request.BranchName.Trim()}";
        var pr = await _gitHubService.CreatePullRequestAsync(sourceBranch, request.TargetBranch.Trim(), request.Title.Trim(),
            request.Draft, request.Description, cancellationToken, request.RepositoryId.Trim());

        if (pr is null || !string.IsNullOrEmpty(pr.Error))
            throw new DomainException(pr?.Error is { Length: > 0 } error ? error : "Erro ao criar o PR no GitHub");

        var register = await _context.PullRequests
            .FirstOrDefaultAsync(x => x.CardNumber == cardNumber, cancellationToken);
        if (register is null)
        {
            // O card ainda não foi salvo: abrir PR já cria o registro (sem descrição/root cause).
            register = new PullRequestRegister(new PullRequestRegisterRequest
            {
                CardNumber = cardNumber,
                UserId = request.UserId,
                FormId = DefaultFormId
            });
            await _context.PullRequests.AddAsync(register, cancellationToken);
        }

        // Idempotência: o mesmo PR (repo + número) pode voltar quando já existia no GitHub.
        var entity = await _context.PullRequestsGithub
            .FirstOrDefaultAsync(x => x.RepositoryId == pr.Repository && x.GithubPrNumber == pr.Number, cancellationToken);
        if (entity is null)
        {
            // Registro legado (migrado do modelo antigo) para a mesma branch/repositório: vira este PR.
            var prefix = request.BranchPrefix?.Trim() ?? string.Empty;
            var name = request.BranchName.Trim();
            var legacy = await _context.PullRequestsGithub
                .FirstOrDefaultAsync(x => x.CardNumber == cardNumber && x.GithubPrNumber == null
                    && x.RepositoryId == pr.Repository && x.BranchPrefix == prefix && x.BranchName == name, cancellationToken);

            if (legacy is not null)
            {
                legacy.PromoteLegacy(request, pr.Number, pr.Id, pr.Url, pr.Status, pr.IsDraft);
                entity = legacy;
            }
            else
            {
                entity = new PullRequestGithub(register, request, pr.Number, pr.Id, pr.Url, pr.Status, pr.IsDraft);
                await _context.PullRequestsGithub.AddAsync(entity, cancellationToken);
            }
        }
        else
        {
            entity.SetContent(request.Title, request.Description);
            entity.SetStatus(pr.Status, pr.IsDraft);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await _realTimeNotifier.NotifyCardUpdatedAsync(cardNumber, PullRequestRealTimeEvents.Actions.GithubPrOpened, entity.Id, cancellationToken);

        var response = entity.ToResponse();
        response.AlreadyExisted = pr.AlreadyExisted;
        return response;
    }

    public async Task<PullRequestGithubResponse> Update(string cardNumber, int id, UpdatePullRequestGithubRequest request, CancellationToken cancellationToken)
    {
        cardNumber = NormalizeCard(cardNumber);
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new DomainException("Título é obrigatório");

        var entity = await _context.PullRequestsGithub
            .FirstOrDefaultAsync(x => x.Id == id && x.CardNumber == cardNumber, cancellationToken)
            ?? throw new DomainException($"PR {id} não encontrado para o card {cardNumber}");
        if (entity.IsLegacy)
            throw new DomainException("Registro legado sem PR no GitHub — use Abrir PR para criá-lo");

        var pr = await _gitHubService.UpdatePullRequestAsync(entity.RepositoryId, entity.GithubPrNumber!.Value,
            request.Title.Trim(), request.Description, cancellationToken);

        if (pr is null || !string.IsNullOrEmpty(pr.Error))
            throw new DomainException(pr?.Error is { Length: > 0 } error ? error : "Erro ao atualizar o PR no GitHub");

        entity.SetContent(request.Title, request.Description);
        entity.SetStatus(pr.Status, pr.IsDraft);
        await _context.SaveChangesAsync(cancellationToken);
        await _realTimeNotifier.NotifyCardUpdatedAsync(cardNumber, PullRequestRealTimeEvents.Actions.GithubPrUpdated, entity.Id, cancellationToken);

        return entity.ToResponse();
    }

    /// <summary>
    /// PRs do card, mais recentes primeiro. Com refreshStatus, só os PRs não terminais (OPEN)
    /// são consultados no GitHub (em paralelo, com cache — ver GitHubService); MERGED/CLOSED
    /// devolvem o status persistido. Mudanças de status são gravadas.
    /// </summary>
    public async Task<IReadOnlyList<PullRequestGithubResponse>> ListByCard(string cardNumber, bool refreshStatus, CancellationToken cancellationToken, bool forceRefresh = false)
    {
        cardNumber = NormalizeCard(cardNumber);
        var entities = await _context.PullRequestsGithub
            .Where(x => x.CardNumber == cardNumber)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var stale = new HashSet<int>();
        if (refreshStatus)
        {
            var open = entities.Where(x => !x.IsLegacy && !PullRequestGithubStatus.IsTerminal(x.Status)).ToList();
            if (open.Count > 0)
            {
                IReadOnlyList<PullRequestStatusResponse> statuses;
                try
                {
                    statuses = await _gitHubService.GetPullRequestsStatusAsync(
                        open.Select(x => (x.RepositoryId, x.GithubPrNumber!.Value)), cancellationToken, forceRefresh);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // GitHub indisponível: a lista sai com o status persistido, marcada como desatualizada.
                    _logger.LogWarning(e, "Falha ao atualizar status dos PRs do card {CardNumber}", cardNumber);
                    statuses = Array.Empty<PullRequestStatusResponse>();
                }
                var byKey = statuses.ToDictionary(s => (s.Repository, s.Number));

                foreach (var entity in open)
                {
                    if (!byKey.TryGetValue((entity.RepositoryId, entity.GithubPrNumber!.Value), out var status)
                        || !string.IsNullOrEmpty(status.Error))
                    {
                        stale.Add(entity.Id);
                        continue;
                    }

                    // Só grava quando muda, para não escrever no banco a cada listagem.
                    if (status.Status != entity.Status || status.IsDraft != entity.IsDraft)
                        entity.SetStatus(status.Status, status.IsDraft);
                }

                if (_context.ChangeTracker.HasChanges())
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    // Outras telas com o card aberto também passam a ver o status novo.
                    await _realTimeNotifier.NotifyCardUpdatedAsync(cardNumber, PullRequestRealTimeEvents.Actions.GithubPrStatusChanged, null, cancellationToken);
                }
            }
        }

        return entities.Select(x =>
        {
            var response = x.ToResponse();
            response.StatusStale = stale.Contains(x.Id);
            return response;
        }).ToList();
    }

    private static string NormalizeCard(string cardNumber)
    {
        var trimmed = cardNumber?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new DomainException("Número do card é obrigatório");
        return trimmed;
    }
}
