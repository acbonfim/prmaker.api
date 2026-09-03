using System.Reflection;
using Cime.BuildingBlocks.Cache;
using Cime.BuildingBlocks.CorsPolice;
using Cime.BuildingBlocks.RealTime;
using Cime.BuildingBlocks.ExceptionHandlerMiddleware;
using Cime.BuildingBlocks.Security;
using Cime.BuildingBlocks.Swagger;
using Cime.BuildingBlocks.GlobalExtensions;
using Microsoft.EntityFrameworkCore;
using solvace.prform.Infra.Contexts;
using solvace.prform.Repositories;
using solvace.github.application.Extensions;
using solvace.azure.application.Extensions;
using solvace.ai.application.Extensions;
using solvace.prform.application;
using solvace.prform.application.Contracts;
using solvace.vacations.application.Contracts;
using solvace.vacations.infra.Extensions;
using solvace.timeline.infra.Extensions;


var builder = WebApplication.CreateBuilder(args);

var assembly = Assembly.GetEntryAssembly();
var projectName = assembly?.GetName().Name;

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<solvace.timeline.application.Contracts.IUserRepository, TimelineUserRepository>();
builder.Services.AddScoped<IFormApplication, FormApplication>();
builder.Services.AddScoped<IPullRequestApplication, PullRequestApplication>();
builder.Services.AddScoped<IHandoverApplication, HandoverApplication>();
builder.Services.AddScoped<IPluginApplication, PluginApplication>();

builder.Services.AddSingleton<IPluginCacheManager, PluginCacheManager>();
builder.Services.AddHostedService<PluginCacheHostedService>();

// Provider de opções de tempo real: lê do plugin "Realtime Configurations" com fallback para o
// ambiente. Registrado antes de AddRealTimeService (que usa TryAdd) para prevalecer sobre o default.
builder.Services.AddSingleton<IRealTimeOptionsProvider, PluginRealTimeOptionsProvider>();

builder.Services
    .AddEndpointsApiExplorer()
    .AddSecurityAuth()
    .AddGlobalServices()
    .AddSwaggerConfig(projectName!)
    .AddCorsPolice()
    .AddRealTimeService(builder.Configuration)
    .AddCacheService()
    .AddGitHubModule(builder.Configuration)
    .AddAzureModule(builder.Configuration)
    .AddAIModule(builder.Configuration)
    .AddVacationModule(builder.Configuration)
    .AddTimelineModule(builder.Configuration);




var connString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<DefaultContext>(x => x.UseMySql(connString, ServerVersion.AutoDetect(connString)));

builder.Services.AddDbContext<AuthenticationContext>(x => x.UseSqlServer(
    builder.Configuration.GetConnectionString("AuthenticationConnection")));


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DefaultContext>();
    
    try
    {
        context.Database.Migrate();
        Console.WriteLine("Migrations do DefaultContext executadas com sucesso.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro ao executar migrations do DefaultContext: {ex.Message}");
    }

    var vacationContext = scope.ServiceProvider.GetRequiredService<solvace.vacations.infra.Contexts.VacationContext>();
    try
    {
        vacationContext.Database.Migrate();
        Console.WriteLine("Migrations do VacationContext executadas com sucesso.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro ao executar migrations do VacationContext: {ex.Message}");
    }

    var timelineContext = scope.ServiceProvider.GetRequiredService<solvace.timeline.infra.Contexts.TimelineContext>();
    try
    {
        timelineContext.Database.Migrate();
        Console.WriteLine("Migrations do TimelineContext executadas com sucesso.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro ao executar migrations do TimelineContext: {ex.Message}");
    }
}

app.UseHttpsRedirection()
    .UseSwaggerConfig(projectName!)
    .UseCors("CorsPolicy")
    .UseMiddleware<ExceptionHandlerMiddleware>();

// Gate de api-key + mapeamento do hub. Antes do MapControllers para garantir que o middleware
// rode cedo no pipeline (antes da execução dos endpoints).
app.UseRealTimeService();

app.MapControllers();
app.AddHealthCheckEndpoint(projectName!);

app.Run();