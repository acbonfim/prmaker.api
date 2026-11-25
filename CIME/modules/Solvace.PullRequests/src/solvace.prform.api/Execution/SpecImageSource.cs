using solvace.azure.application.Contract;
using solvace.executionplans.application.Contracts;

namespace solvace.prform.Execution;

/// <summary>
/// Card 78525: imagem <c>anexo:nome</c> da especificação da US — o mesmo arquivo que a tela mostra pelo nome (planos e
/// itens de trabalho do card), procurado nos cards na ordem.
/// </summary>
/// O serviço do plano é resolvido na hora (não no construtor): ele depende das ações do DevOps, que dependem desta fonte.
public class SpecImageSource(IServiceProvider services) : ISpecImageSource
{
    public async Task<(string Name, byte[] Content)?> FindAsync(IReadOnlyList<string> cards, string name, CancellationToken cancellationToken)
    {
        var plans = services.GetRequiredService<IExecutionPlanApplication>();
        foreach (var card in cards)
        {
            try
            {
                var file = await plans.GetCardArtifactFileByNameAsync(card, name, cancellationToken);
                if (file.Data.Length > 0)
                    return (file.Artifact.Name, file.Data);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // não está neste card — tenta o próximo
            }
        }
        return null;
    }
}
