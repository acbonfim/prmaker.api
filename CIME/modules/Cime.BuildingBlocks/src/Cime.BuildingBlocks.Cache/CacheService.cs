
using Microsoft.Extensions.Caching.Memory;

namespace Cime.BuildingBlocks.Cache;

public interface ICacheService
{
    /// <summary>
    /// Salva um valor no cache com a chave especificada
    /// </summary>
    /// <typeparam name="T">Tipo do valor a ser armazenado</typeparam>
    /// <param name="key">Chave única para identificar o valor no cache</param>
    /// <param name="value">Valor a ser armazenado</param>
    /// <param name="expirationMinutes">Tempo de expiração em minutos (padrão: 60)</param>
    void Set<T>(string key, T value, int expirationMinutes = 60);

    /// <summary>
    /// Recupera um valor do cache pela chave
    /// </summary>
    /// <typeparam name="T">Tipo do valor armazenado</typeparam>
    /// <param name="key">Chave do valor no cache</param>
    /// <returns>O valor armazenado ou default(T) se não encontrado</returns>
    T? Get<T>(string key);

    /// <summary>
    /// Tenta recuperar um valor do cache pela chave
    /// </summary>
    /// <typeparam name="T">Tipo do valor armazenado</typeparam>
    /// <param name="key">Chave do valor no cache</param>
    /// <param name="value">Valor recuperado do cache</param>
    /// <returns>True se o valor foi encontrado, False caso contrário</returns>
    bool TryGetValue<T>(string key, out T? value);

    /// <summary>
    /// Remove um valor do cache pela chave
    /// </summary>
    /// <param name="key">Chave do valor a ser removido</param>
    void Remove(string key);

    /// <summary>
    /// Recupera um valor do cache ou executa a função para obtê-lo e armazená-lo
    /// </summary>
    /// <typeparam name="T">Tipo do valor</typeparam>
    /// <param name="key">Chave do cache</param>
    /// <param name="factory">Função para obter o valor caso não esteja em cache</param>
    /// <param name="expirationMinutes">Tempo de expiração em minutos (padrão: 60)</param>
    /// <returns>O valor do cache ou o valor retornado pela função</returns>
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, int expirationMinutes = 60);

    /// <summary>
    /// Verifica se uma chave existe no cache
    /// </summary>
    /// <param name="key">Chave a ser verificada</param>
    /// <returns>True se a chave existe, False caso contrário</returns>
    bool Exists(string key);
}

public class CacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;

    public CacheService(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
    }

    public void Set<T>(string key, T value, int expirationMinutes = 60)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A chave não pode ser nula ou vazia.", nameof(key));

        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(expirationMinutes),
            Priority = CacheItemPriority.Normal
        };

        _memoryCache.Set(key, value, cacheOptions);
    }

    public T? Get<T>(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A chave não pode ser nula ou vazia.", nameof(key));

        return _memoryCache.TryGetValue(key, out T? value) ? value : default;
    }

    public bool TryGetValue<T>(string key, out T? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A chave não pode ser nula ou vazia.", nameof(key));

        return _memoryCache.TryGetValue(key, out value);
    }

    public void Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A chave não pode ser nula ou vazia.", nameof(key));

        _memoryCache.Remove(key);
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, int expirationMinutes = 60)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A chave não pode ser nula ou vazia.", nameof(key));

        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        if (_memoryCache.TryGetValue(key, out T? cachedValue) && cachedValue != null)
        {
            return cachedValue;
        }

        var value = await factory();
        Set(key, value, expirationMinutes);
        return value;
    }

    public bool Exists(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A chave não pode ser nula ou vazia.", nameof(key));

        return _memoryCache.TryGetValue(key, out _);
    }
}