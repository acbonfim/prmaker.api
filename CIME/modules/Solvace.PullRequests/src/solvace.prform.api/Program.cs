using System.Reflection;
using Cime.BuildingBlocks.Cache;
using Cime.BuildingBlocks.CorsPolice;
using Cime.BuildingBlocks.RealTime;
using Cime.BuildingBlocks.ExceptionHandlerMiddleware;
using Cime.BuildingBlocks.Security;
using Cime.BuildingBlocks.Swagger;
using Cime.BuildingBlocks.GlobalExtensions;
using Microsoft.EntityFrameworkCore;
using solvace.prform.api.Auditing;
using solvace.prform.api.Startup;
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

// Segredos de produção (0016): um único secret JSON por serviço no Secret Manager, montado pelo
// Cloud Run como arquivo (cabe na cota grátis). Tem precedência sobre appsettings e variáveis de
// ambiente; ausente no dev. SECRETS_FILE permite outro caminho (testes locais).
var secretsFile = Environment.GetEnvironmentVariable("SECRETS_FILE") ?? "/secrets/appsettings.secrets.json";
builder.Configuration.AddJsonFile(secretsFile, optional: true, reloadOnChange: false);

var assembly = Assembly.GetEntryAssembly();
var projectName = assembly?.GetName().Name;

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<solvace.timeline.application.Contracts.IUserRepository, TimelineUserRepository>();
builder.Services.AddScoped<IFormApplication, FormApplication>();
builder.Services.AddScoped<IPullRequestApplication, PullRequestApplication>();
builder.Services.AddScoped<IHandoverApplication, HandoverApplication>();
builder.Services.AddScoped<IPluginApplication, PluginApplication>();

builder.Services.AddSingleton<IPluginCacheManager, PluginCacheManager>();
// Integrações pessoais (feature 0002): segredos dos usuários criptografados com a chave de UserIntegrations.
builder.Services.Configure<solvace.prform.application.Security.UserIntegrationOptions>(
    builder.Configuration.GetSection(solvace.prform.application.Security.UserIntegrationOptions.SectionName));
builder.Services.AddSingleton<solvace.prform.application.Security.ISecretProtector, solvace.prform.application.Security.AesGcmSecretProtector>();
builder.Services.AddScoped<solvace.prform.application.UserIntegrations.IUserPluginConfigurationApplication, solvace.prform.application.UserIntegrations.UserPluginConfigurationApplication>();
builder.Services.AddScoped<solvace.prform.application.UserIntegrations.IPluginConfigurationResolver, solvace.prform.application.UserIntegrations.PluginConfigurationResolver>();
// Pedido de aprovação de PR no Teams via Workflow (feature 0007).
builder.Services.AddScoped<solvace.prform.application.Teams.ITeamsApprovalService, solvace.prform.application.Teams.TeamsApprovalService>();
builder.Services.AddHttpClient(solvace.prform.application.Teams.TeamsApprovalService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.Filters.Add<solvace.prform.api.Filters.PersonalIntegrationExceptionFilter>());
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




// PostgreSQL (feature 0015): os 3 contextos do host (prform, vacations, timeline), cada um no seu
// schema. Chave nova de propósito: a versão anterior (MySQL) lia "DefaultConnection", que continua
// existindo para o rollback.
var connString = builder.Configuration.GetConnectionString("PrformDatabase");

// Auditoria automática (CreatedBy/UpdatedBy) a partir do usuário autenticado.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();

builder.Services.AddDbContext<DefaultContext>((sp, x) => x
    // Banco remoto (MonsterASP) via internet pública: habilita retry em falhas transitórias.
    .UseNpgsql(connString, npgsql => npgsql
        .MigrationsHistoryTable("__EFMigrationsHistory", DefaultContext.Schema)
        .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null))
    .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));

// Usuários da Cime.Auth (schema "auth"), no mesmo database dos módulos do host (0016).
builder.Services.AddDbContext<AuthenticationContext>(x => x.UseNpgsql(
    connString,
    npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null)));


var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    // Migrations dos 3 contexts, protegidas por advisory lock do PostgreSQL para não
    // haver corrida entre instâncias. Falha aqui é FATAL de propósito: a app não sobe e o
    // Cloud Run mantém a revisão anterior servindo em vez de publicar um schema quebrado.
    await app.MigratePostgresWithLockAsync();
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