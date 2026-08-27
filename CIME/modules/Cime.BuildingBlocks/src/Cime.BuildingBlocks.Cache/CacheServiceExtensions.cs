using Microsoft.Extensions.DependencyInjection;

namespace Cime.BuildingBlocks.Cache;

public static class CacheServiceExtensions
{
    /// <summary>
    /// Adiciona o serviço de cache ao container de injeção de dependência
    /// </summary>
    /// <param name="services">Coleção de serviços</param>
    /// <returns>A coleção de serviços para encadeamento</returns>
    public static IServiceCollection AddCacheService(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<ICacheService, CacheService>();
        return services;
    }
}