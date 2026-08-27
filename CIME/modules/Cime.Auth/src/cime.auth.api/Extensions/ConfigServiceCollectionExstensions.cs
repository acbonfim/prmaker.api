using cliqx.auth.api.Services.Application;
using ProAuth.Services.Application;
using ProAuth.Services.Contracts;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ConfigServiceCollectionExtensions
    {

        public static IServiceCollection AddMyDependencyGroup(
             this IServiceCollection services)
        {
            // services.AddScoped<IGlobalRepository, GlobalRepository>();
             services.AddScoped<IUserService, UserService>();
             services.AddScoped<IRoleService, RoleService>();
             services.AddScoped<IPasswordService, PasswordService>();
             services.AddScoped<MyServicesService>();

            return services;
        }
    }
}