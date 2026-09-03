using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Resolve a policy de CORS do hub por request a partir do <see cref="IRealTimeOptionsProvider"/>,
/// para que mudanças de origens (ex.: via plugin) sejam refletidas sem reiniciar a aplicação.
/// As demais policies (ex.: a global "CorsPolicy") são delegadas ao provider padrão.
/// </summary>
public class RealTimeCorsPolicyProvider : ICorsPolicyProvider
{
    private readonly DefaultCorsPolicyProvider _defaultProvider;
    private readonly IRealTimeOptionsProvider _optionsProvider;

    public RealTimeCorsPolicyProvider(IOptions<CorsOptions> options, IRealTimeOptionsProvider optionsProvider)
    {
        _defaultProvider = new DefaultCorsPolicyProvider(options);
        _optionsProvider = optionsProvider;
    }

    public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        if (policyName != RealTimeServiceExtensions.CorsPolicyName)
            return _defaultProvider.GetPolicyAsync(context, policyName);

        var options = _optionsProvider.GetOptions();

        var builder = new CorsPolicyBuilder()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();

        if (options.AllowedOrigins is { Length: > 0 })
            builder.WithOrigins(options.AllowedOrigins);
        else
            builder.SetIsOriginAllowed(_ => true);

        return Task.FromResult<CorsPolicy?>(builder.Build());
    }
}
