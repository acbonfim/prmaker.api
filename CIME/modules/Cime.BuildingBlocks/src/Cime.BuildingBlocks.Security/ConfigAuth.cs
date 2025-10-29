using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Cime.BuildingBlocks.Security
{
    public static class ConfigAuth
    {
        public static IServiceCollection AddSecurityAuth(this IServiceCollection services)
        {
            services.AddAuthentication("XApiKey")
                .AddScheme<AuthenticationSchemeOptions, XApiKeyAuthenticationHandler>("XApiKey", options => { });
                

            services.AddAuthorization(auth =>
            {
                auth.AddPolicy("XApiKey", new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes("XApiKey")
                    .RequireAuthenticatedUser().Build());
            });

            services.AddMvc(options =>
            {
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
                options.Filters.Add(new AuthorizeFilter(policy));
            });

            return services;
        }
    }
}
