using System.Reflection;
using Cime.BuildingBlocks.CorsPolice;
using Cime.BuildingBlocks.ExceptionHandlerMiddleware;
using Cime.BuildingBlocks.Security;
using Cime.BuildingBlocks.Swagger;
using Cime.BuildingBlocks.GlobalExtensions;
using Microsoft.EntityFrameworkCore;
using solvace.prform.Infra.Contexts;
using solvace.github.application.Extensions;
using solvace.azure.application.Extensions;
using solvace.ai.application.Extensions;
using solvace.prform.application;
using solvace.prform.application.Contracts;


var builder = WebApplication.CreateBuilder(args);

var assembly = Assembly.GetEntryAssembly();
var projectName = assembly?.GetName().Name;

builder.Services
    .AddEndpointsApiExplorer()
    .AddSecurityAuth()
    .AddGlobalServices()
    .AddSwaggerConfig(projectName!)
    .AddCorsPolice()
    .AddGitHubModule(builder.Configuration)
    .AddAzureModule(builder.Configuration)
    .AddAIModule(builder.Configuration);

builder.Services.AddScoped<IFormApplication, FormApplication>();
builder.Services.AddScoped<IPullRequestApplication, PullRequestApplication>();

var connString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<DefaultContext>(x => x.UseMySql(connString, ServerVersion.AutoDetect(connString)));


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DefaultContext>();
    try
    {
        context.Database.Migrate();
        Console.WriteLine("Migrations executadas com sucesso.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro ao executar migrations: {ex.Message}");
    }
}

app.UseHttpsRedirection()
    .UseSwaggerConfig(projectName!)
    .UseCors("CorsPolicy")
    .UseMiddleware<ExceptionHandlerMiddleware>();

app.MapControllers();
app.AddHealthCheckEndpoint(projectName!);

app.Run();