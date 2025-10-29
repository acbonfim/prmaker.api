using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cime.BuildingBlocks.GlobalExtensions
{
    public static class HealthCheckEndpointExtension
    {
        public static IEndpointRouteBuilder AddHealthCheckEndpoint(this IEndpointRouteBuilder endpoints, string projectName)
        {
            endpoints.MapGet("/", () =>
            {
                return Results.Ok($"API {projectName} ON. Environment: " + endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().EnvironmentName);
            })
            .Produces<string>(StatusCodes.Status200OK)
            .AllowAnonymous();

            return endpoints;
        }
    }
}