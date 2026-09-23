namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Resolve as opções de tempo real em runtime. A implementação padrão usa apenas o appsettings;
/// o host pode substituí-la para buscar de outra fonte (ex.: plugin configuration) com fallback
/// para as configurações de ambiente.
/// </summary>
public interface IRealTimeOptionsProvider
{
    RealTimeOptions GetOptions();
}
