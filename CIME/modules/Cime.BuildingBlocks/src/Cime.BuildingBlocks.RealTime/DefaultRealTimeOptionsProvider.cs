using Microsoft.Extensions.Options;

namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Implementação padrão: lê as opções apenas do appsettings (seção "RealTime").
/// </summary>
public class DefaultRealTimeOptionsProvider : IRealTimeOptionsProvider
{
    private readonly RealTimeOptions _options;

    public DefaultRealTimeOptionsProvider(IOptions<RealTimeOptions> options)
    {
        _options = options.Value;
    }

    public RealTimeOptions GetOptions() => _options;
}
