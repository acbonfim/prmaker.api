namespace Cime.BuildingBlocks.RealTime;

/// <summary>Dados para o navegador abrir a conexão de tempo real.</summary>
/// <param name="Url">URL do hub; nula => o front usa a do próprio environment.</param>
/// <param name="AccessToken">Token de curta duração; nulo quando a API não tem TokenSigningKey.</param>
public record RealTimeConnectionInfo(string? Url, string? AccessToken, DateTime? ExpiresAt);

public interface IRealTimeConnectionService
{
    RealTimeConnectionInfo GetConnectionInfo(string? userId);
}

public class RealTimeConnectionService : IRealTimeConnectionService
{
    private readonly IRealTimeOptionsProvider _optionsProvider;

    public RealTimeConnectionService(IRealTimeOptionsProvider optionsProvider)
    {
        _optionsProvider = optionsProvider;
    }

    public RealTimeConnectionInfo GetConnectionInfo(string? userId)
    {
        var options = _optionsProvider.GetOptions();

        string? url = options.PublicHubUrl;
        if (string.IsNullOrWhiteSpace(url) && options.IsRelay && !string.IsNullOrWhiteSpace(options.RelayUrl))
            url = options.RelayUrl.TrimEnd('/') + options.HubPath;

        if (string.IsNullOrWhiteSpace(options.TokenSigningKey))
            return new RealTimeConnectionInfo(url, null, null);

        var lifetime = options.TokenLifetimeMinutes > 0 ? options.TokenLifetimeMinutes : 10;
        var expiresAt = DateTime.UtcNow.AddMinutes(lifetime);
        return new RealTimeConnectionInfo(url, RealTimeTokens.Create(options.TokenSigningKey, userId, expiresAt), expiresAt);
    }
}
