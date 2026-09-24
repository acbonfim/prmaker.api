namespace solvace.prform.domain.Requests;

/// <summary>
/// Valores do usuário para um plugin de uso pessoal (PUT /UserIntegration/{pluginId}).
/// Chave omitida ou null = mantém o valor salvo (útil para segredos, que nunca voltam ao front);
/// string vazia = limpa.
/// </summary>
public class SaveUserIntegrationRequest
{
    public Dictionary<string, string?> Values { get; set; } = new();
}
