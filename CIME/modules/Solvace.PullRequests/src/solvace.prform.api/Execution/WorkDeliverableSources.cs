using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Responses;
using Microsoft.EntityFrameworkCore;
using solvace.github.application.Contract;
using solvace.prform.Infra.Contexts;

namespace solvace.prform.Execution;

/// <summary>
/// 0091: PRs do card (do banco, sem ir ao GitHub — o estado é o da última sincronização da tela/plano) e o compare do
/// GitHub por repositório. O serviço do GitHub é resolvido sob demanda: exige o plugin no construtor, e o item tem de
/// abrir mesmo sem a integração.
/// </summary>
public class WorkDeliverableSources(IServiceProvider services) : IWorkDeliverableSources
{
    /// <summary>Do banco (<c>PullRequestsGithub</c>), sem o serviço do GitHub: abre mesmo sem a integração de quem lê.</summary>
    public async Task<IReadOnlyList<DeliverableCardPr>> ListCardPullRequestsAsync(string cardNumber, CancellationToken ct)
    {
        var card = (cardNumber ?? string.Empty).Trim();
        var rows = await services.GetRequiredService<DefaultContext>().PullRequestsGithub.AsNoTracking()
            .Where(pr => pr.CardNumber == card && pr.GithubPrNumber != null)
            .OrderBy(pr => pr.CreatedAt)
            .Select(pr => new { pr.RepositoryId, pr.GithubPrNumber, pr.Url, pr.TargetBranch, pr.BranchPrefix, pr.BranchName, pr.Status, pr.IsDraft, pr.CreatedAt })
            .ToListAsync(ct);
        return rows
            .Where(pr => !string.IsNullOrWhiteSpace(pr.Url))
            .Select(pr => new DeliverableCardPr(pr.RepositoryId, pr.GithubPrNumber!.Value, pr.Url, pr.TargetBranch,
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
        solvace.github.domain.Responses.CompareDiffResponse? compare;
        try
        {
            var github = services.GetRequiredService<IGitHubService>();
            compare = await github.CompareRefsDiffAsync(@base, head, ct, repository);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Sem a integração do GitHub (plugin/credencial) ou GitHub fora: a tela mostra o link do PR no lugar do diff.
            return new WorkDeliverableDiffResponse
            {
                Repository = repository, Base = @base, Head = head,
                Error = $"Não deu para ler as alterações no GitHub: {e.Message}",
            };
        }
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
