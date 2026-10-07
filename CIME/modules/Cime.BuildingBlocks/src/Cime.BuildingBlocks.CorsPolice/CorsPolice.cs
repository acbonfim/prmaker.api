using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Cime.BuildingBlocks.CorsPolice
{
    public static class CorsPolice
    {
        public static IServiceCollection AddCorsPolice(this IServiceCollection services)
        {
            services.AddCors(options =>
                {
                    options.AddPolicy("CorsPolicy", builder => builder
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    // Consumo de IA da requisição (0042): o front lê para mostrar tokens/custo depois da ação.
                    .WithExposedHeaders("X-AI-Usage", "Server-Timing")
                    // 0070: o navegador guarda o preflight (OPTIONS) — sem isto cada GET custava duas idas ao servidor
                    // (o Chrome limita a 2 h).
                    .SetPreflightMaxAge(TimeSpan.FromHours(2)));
                });

            return services;
        }
    }
}