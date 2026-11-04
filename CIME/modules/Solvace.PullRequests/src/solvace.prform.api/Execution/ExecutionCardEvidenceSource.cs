using solvace.azure.application.Contract;
using solvace.azure.domain.Models;
using solvace.executionplans.application.Contracts;

namespace solvace.prform.Execution;

/// <summary>0076: evidências do card no DevOps (com a integração do DevOps de quem chama) para o plano de execução.</summary>
public class ExecutionCardEvidenceSource(IAzureService azure) : IExecutionCardEvidenceSource
{
    public async Task<IReadOnlyList<CardEvidence>> ListAsync(string cardNumber, CancellationToken cancellationToken) =>
        (await azure.GetCardEvidenceAsync(cardNumber.Trim(), cancellationToken))
            .Select(e => new CardEvidence(e.Key, e.Url, e.FileName, e.Source, e.SourceDetail, e.At, e.Size, e.Comment))
            .ToList();

    public async Task<CardEvidenceFile> DownloadAsync(CardEvidence evidence, long maxBytes, CancellationToken cancellationToken)
    {
        var file = await azure.DownloadAttachmentAsync(evidence.Url, maxBytes, cancellationToken);
        return new CardEvidenceFile(file.Data, file.ContentType, file.TooLarge, file.Size);
    }
}
