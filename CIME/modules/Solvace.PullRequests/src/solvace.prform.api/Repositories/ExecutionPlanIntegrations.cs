using solvace.executionplans.application.Contracts;
using solvace.github.application.Contract;
using solvace.timeline.application.Contracts;
using solvace.timeline.domain.Entities;
using solvace.timeline.domain.Requests;

namespace solvace.prform.Repositories;

/// <summary>
/// PRs do card para o plano de execução (0024): a mesma sincronização da tela (status do GitHub com cache
/// de 60 s e a credencial pessoal de quem consulta).
/// </summary>
public class ExecutionPlanPullRequestSource(IPullRequestGithubApplication github) : IExecutionPullRequestSource
{
    public async Task<IReadOnlyList<CardPullRequest>> ListByCardAsync(string cardNumber, CancellationToken cancellationToken)
    {
        var prs = await github.ListByCard(cardNumber, refreshStatus: true, cancellationToken);
        return prs
            .Select(pr => new
            {
                pr,
                status = pr.Status?.ToUpperInvariant() switch
                {
                    "OPEN" => "open",
                    "MERGED" => "merged",
                    "CLOSED" => "closed",
                    _ => null // LEGACY (sem PR no GitHub)
                }
            })
            .Where(x => x.status is not null && x.pr.Number is not null)
            .Select(x => new CardPullRequest(x.pr.RepositoryId, x.pr.Number, x.pr.Url, x.pr.TargetBranch,
                x.pr.BranchPrefix + x.pr.BranchName, x.status!, x.pr.Title))
            .ToList();
    }
}

/// <summary>Marcos do plano de execução na Timeline do card (0024).</summary>
public class ExecutionPlanTimelineWriter(ITimelineApplication timeline) : IExecutionTimelineWriter
{
    public async Task WriteAsync(string cardNumber, string markdown, Guid? userId, string actorName, CancellationToken cancellationToken)
    {
        var text = markdown.Length <= TimelineEntry.MaxDescriptionLength ? markdown : markdown[..TimelineEntry.MaxDescriptionLength];
        var request = new CreateTimelineEntryRequest { CardNumber = cardNumber, Description = text, UserName = actorName };
        try
        {
            await timeline.CreateAsync(request, userId, cancellationToken);
        }
        catch (solvace.timeline.domain.Entities.Base.DomainException) when (userId is not null)
        {
            // Usuário não encontrado na base de autenticação: registra com o nome, como externo.
            await timeline.CreateAsync(request, null, cancellationToken);
        }
    }
}
