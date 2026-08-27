using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Cime.BuildingBlocks.Swagger;
public static class ConfigSwagger
{
    public static IServiceCollection AddSwaggerConfig(
        this IServiceCollection services, string projectName)
    {
        var t = new UtilsSwaggerHTML();

        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
        });

        services.AddSwaggerGen(c =>
        {


            c.SwaggerDoc("v1",
                new OpenApiInfo
                {
                    Title = projectName,
                    Version = "v1",
                    Description = t.MontaDescricaoAPI()

                });

            c.SwaggerDoc("v2",
            new OpenApiInfo
            {
                Title = projectName,
                Version = "v2",
                Description = "Pendente montar documentação pra V2 da API",
            });

            c.AddSecurityDefinition("XApiKey", new OpenApiSecurityScheme()
            {
                Name = "x-api-key",
                Type = SecuritySchemeType.ApiKey,
                Scheme = "XApiKey",
                In = ParameterLocation.Header,
                Description = "API Key Authentication header. Example: \"x-api-key: your-api-key\"",
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "XApiKey"
                        }
                    },
                    new string[] {}
                }
            });

            c.DocInclusionPredicate((version, desc) =>
            {
                if (!desc.TryGetMethodInfo(out var methodInfo)) return false;

                var versions = methodInfo.DeclaringType?
                    .GetCustomAttributes(true)
                    .OfType<ApiVersionAttribute>()
                    .SelectMany(attr => attr.Versions);

                return versions?.Any(v => $"v{v.MajorVersion}" == version) ?? false;
            });


            var xmlFile = $"{projectName}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            //c.IncludeXmlComments(xmlPath);


        });

        services.AddApiVersioning(options =>
        {
            options.ReportApiVersions = true;
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.DefaultApiVersion = new ApiVersion(1, 0);
        });

        return services;
    }

    public static IApplicationBuilder UseSwaggerConfig(this IApplicationBuilder app, string projectName)
    {
        app.UseApiVersioning();
        
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", $"{projectName} V1");
            c.SwaggerEndpoint("/swagger/v2/swagger.json", $"{projectName} V2");

            c.RoutePrefix = "swagger";
        });

        return app;
    }

}
