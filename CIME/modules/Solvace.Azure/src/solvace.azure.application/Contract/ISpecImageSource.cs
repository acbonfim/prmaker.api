namespace solvace.azure.application.Contract;

/// <summary>
/// Card 78525: arquivo citado como <c>![...](anexo:nome.png)</c> no texto que vai para um campo do card — o PRMake procura
/// pelo nome nos arquivos dos cards informados (planos e itens de trabalho). Null = não achou.
/// </summary>
public interface ISpecImageSource
{
    Task<(string Name, byte[] Content)?> FindAsync(IReadOnlyList<string> cards, string name, CancellationToken cancellationToken);
}
