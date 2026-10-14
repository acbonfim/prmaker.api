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
using solvace.executionplans.infra.Extensions;
using solvace.knowledge.infra.Extensions;


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
// Plano de execução (0024): PRs do card e marcos na Timeline, sobre os módulos GitHub e Timeline.
builder.Services.AddScoped<solvace.executionplans.application.Contracts.IExecutionPullRequestSource, ExecutionPlanPullRequestSource>();
builder.Services.AddScoped<solvace.executionplans.application.Contracts.IExecutionTimelineWriter, ExecutionPlanTimelineWriter>();
builder.Services.AddScoped<solvace.executionplans.application.Contracts.IExecutionCardRegistrar, ExecutionPlanCardRegistrar>();
// Fila de execução e executores (0039): credencial do executor, binários publicados e a regra automática (WIQL).
builder.Services.AddSingleton<solvace.prform.Execution.ExecutorTokenIssuer>();
builder.Services.AddSingleton<solvace.prform.Execution.ExecutionAgentCatalog>();
builder.Services.AddSingleton<solvace.executionplans.application.Contracts.IExecutionAgentInfo>(sp => sp.GetRequiredService<solvace.prform.Execution.ExecutionAgentCatalog>());
builder.Services.AddScoped<solvace.executionplans.application.Contracts.IExecutionWorkItemSource, solvace.prform.Execution.ExecutionWorkItemSource>();
builder.Services.AddScoped<solvace.executionplans.application.Contracts.IExecutionQueueSettings, solvace.prform.Execution.ExecutionQueueSettings>();
// 0041: REST e MCP com a mesma lógica (configuração das skills; ação do DevOps + Timeline).
builder.Services.AddScoped<solvace.prform.Skills.SkillsConfigService>();
builder.Services.AddScoped<solvace.prform.Home.HomeCardsService>();
builder.Services.AddScoped<solvace.prform.Admin.CardResetService>();  // 0061: recomeçar um card
builder.Services.AddScoped<solvace.prform.Execution.DevOpsActionRunner>();
builder.Services.AddMemoryCache();
// 0070: respostas comprimidas (documentos da engenharia reversa chegam a 1 MB de markdown; o export, a 8 MB).
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    o.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(["text/markdown", "application/problem+json"]);
});
// MCP remoto do PRMake (0039): /mcp, Streamable HTTP sem sessão (Cloud Run), autenticado pela x-api-key.
builder.Services.AddMcpServer(o =>
    {
        o.ServerInfo = new ModelContextProtocol.Protocol.Implementation { Name = "prmake", Version = "1.0.0" };
        o.ServerInstructions = solvace.prform.Execution.PrmakeMcpTools.Instructions;
    })
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<solvace.prform.Execution.PrmakeMcpTools>()
    // 0052: Base Solvace (engenharia reversa por item, seções, KC) pelo MCP — a análise consulta antes do código.
    .WithTools<solvace.prform.Execution.PrmakeBaseMcpTools>();
// Skills do Claude Code publicadas pelo PRMake (0024): pasta skills/ copiada para a imagem.
builder.Services.AddSingleton<solvace.prform.Skills.SkillsCatalog>();
// Base de conhecimento Solvace (0033): configuração do KC vem do plugin "Knowledge Center Configurations".
builder.Services.AddScoped<solvace.knowledge.application.Contracts.IKnowledgeSettingsProvider, solvace.prform.Knowledge.PluginKnowledgeSettingsProvider>();
// Engenharia reversa por módulo (0052): aprovadores, documentos exigidos e trava vêm do "Skills Configurations".
builder.Services.AddScoped<solvace.knowledge.application.Contracts.IReverseSettingsProvider, solvace.prform.Knowledge.PluginReverseSettingsProvider>();
builder.Services.AddScoped<solvace.prform.Knowledge.ArchitectureChatService>();
builder.Services.AddScoped<solvace.prform.Knowledge.ArchitectureAskService>();
builder.Services.AddScoped<solvace.prform.Knowledge.ArchitectureGuideService>();
builder.Services.AddScoped<solvace.prform.Knowledge.ArchitectureLearnService>();
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
    .AddTimelineModule(builder.Configuration)
    .AddExecutionPlanModule(builder.Configuration)
    .AddKnowledgeModule(builder.Configuration);




// PostgreSQL (feature 0015): os contextos do host (prform, vacations, timeline, execution — 0023), cada um no seu
// schema. Chave nova de propósito: a versão anterior (MySQL) lia "DefaultConnection", que continua
// existindo para o rollback.
var connString = builder.Configuration.GetConnectionString("PrformDatabase");

// Auditoria automática (CreatedBy/UpdatedBy) a partir do usuário autenticado.
builder.Services.AddHttpContextAccessor();
// Consumo de IA por ação (0042): o PluginAIService chama o recorder a cada geração.
builder.Services.AddScoped<solvace.ai.application.Contract.IAIUsageRecorder, solvace.prform.AiUsage.AiUsageRecorder>();
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
// Migrações fora do startup (0018): o pipeline roda esta mesma imagem com "--migrate" (Cloud Run
// Job) antes de publicar a revisão; se falhar, o deploy não acontece. Subindo normalmente, a API
// não migra (cold start menor). Localmente: dotnet run -- --migrate.
if (args.Contains("--migrate"))
{
    // Migrations de todos os contexts, protegidas por advisory lock do PostgreSQL. Falha => exit 1 na
    // hora (o pipeline para). Não depende da exceção "não tratada": no teste o processo ficou vivo.
    try
    {
        await app.MigratePostgresWithLockAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "Falha ao aplicar as migrations.");
        Environment.Exit(1);
    }
    return;
}



// 0070: Server-Timing + log de requisição lenta; compressão fora do MCP (Streamable HTTP) e do hub de tempo real.
EfCommandObserver.Start();
app.UseMiddleware<RequestTimingMiddleware>();
var realTimeHubPath = builder.Configuration.GetSection("RealTime")["HubPath"] ?? "/ws";
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/mcp") && !ctx.Request.Path.StartsWithSegments(realTimeHubPath),
    branch => branch.UseResponseCompression());

app.UseHttpsRedirection()
    .UseSwaggerConfig(projectName!)
    .UseCors("CorsPolicy")
    .UseMiddleware<ExceptionHandlerMiddleware>()
    // 0039: credencial do executor só na fila/plano/skills/MCP e revogável na hora.
    .UseMiddleware<solvace.prform.Execution.ExecutorCredentialMiddleware>();

// Gate de api-key + mapeamento do hub. Antes do MapControllers para garantir que o middleware
// rode cedo no pipeline (antes da execução dos endpoints).
app.UseRealTimeService();

app.MapControllers();
// 0070: memória viva depois de uma coleta completa — só no Development (medição local antes/depois)
if (app.Environment.IsDevelopment())
    app.MapGet("/debug/memory", () =>
    {
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        var info = GC.GetGCMemoryInfo();
        return Results.Ok(new { liveMb = GC.GetTotalMemory(false) / 1048576, heapMb = info.HeapSizeBytes / 1048576, committedMb = info.TotalCommittedBytes / 1048576,
            rssMb = Environment.WorkingSet / 1048576 });
    }).AllowAnonymous();
app.MapMcp("/mcp").RequireAuthorization();
app.AddHealthCheckEndpoint(projectName!);

app.Run();