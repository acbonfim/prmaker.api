using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Responses;
using solvace.github.application.Contract;

namespace solvace.prform.Execution;

/// <summary>
/// 0091: PRs do card (do banco, sem ir ao GitHub — o estado é o da última sincronização da tela/plano) e o compare do
/// GitHub por repositório. Os serviços do GitHub são resolvidos sob demanda: exigem o plugin no construtor, e o item
/// tem de abrir mesmo sem a integração.
/// </summary>
public class WorkDeliverableSources(IServiceProvider services) : IWorkDeliverableSources
{
    public async Task<IReadOnlyList<DeliverableCardPr>> ListCardPullRequestsAsync(string cardNumber, CancellationToken ct)
    {
        var github = services.GetRequiredService<IPullRequestGithubApplication>();
        var prs = await github.ListByCard(cardNumber, refreshStatus: false, ct);
        return prs
            .Where(pr => pr.Number is not null && !string.IsNullOrWhiteSpace(pr.Url))
            .Select(pr => new DeliverableCardPr(pr.RepositoryId, pr.Number!.Value, pr.Url!, pr.TargetBranch,
                pr.BranchPrefix + pr.BranchName, (pr.Status ?? string.Empty).ToUpperInvariant() switch
                {
                    "MERGED" => "merged",
                    "CLOSED" => "closed",
                    _ => pr.IsDraft ? "draft" : "open",
                }, pr.CreatedAt))
            .ToList();
    }

    public async Task<WorkDeliverableDiffResponse> CompareAsync(string repository, string @base, string head, CancellationToken ct)
    {
        var github = services.GetRequiredService<IGitHubService>();
        var compare = await github.CompareRefsDiffAsync(@base, head, ct, repository);
        if (compare is null || !string.IsNullOrWhiteSpace(compare.Error))
            return new WorkDeliverableDiffResponse
            {
                Repository = repository, Base = @base, Head = head,
                Error = $"Não deu para ler as alterações no GitHub ({compare?.Error ?? "sem resposta"}) — a branch pode ter sido apagada depois do merge.",
            };
        return new WorkDeliverableDiffResponse
        {
            Repository = repository,
            Base = @base,
            Head = head,
            CompareUrl = compare.HtmlUrl,
            // A API do GitHub devolve no máximo 300 arquivos no compare.
            Truncated = compare.Files.Length >= 300,
            Files = compare.Files.Select(f => new WorkDeliverableDiffFile
            {
                Path = f.Filename,
                Status = f.Status is "added" or "removed" or "renamed" ? f.Status : "modified",
                Additions = f.Additions,
                Deletions = f.Deletions,
                Patch = f.Patch,
            }).ToList(),
        };
    }
}
