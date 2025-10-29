using System.Reflection;
using Cliqx.BuildingBlocks.CorsPolice;
using Cliqx.BuildingBlocks.ExceptionHandlerMiddleware;
using Cliqx.BuildingBlocks.GlobalExtensions;
using Cliqx.BuildingBlocks.Security;
using Cliqx.BuildingBlocks.Swagger;
using Microsoft.EntityFrameworkCore;
using solvace.prform.Infra.Contexts;


var builder = WebApplication.CreateBuilder(args);

var assembly = Assembly.GetEntryAssembly();
var projectName = assembly?.GetName().Name;

builder.Services
    .AddEndpointsApiExplorer()
    .AddSecurityAuth()
    .AddGlobalServices()
    .AddSwaggerConfig(projectName!)
    .AddCorsPolice();

var connString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<DefaultContext>(x => x.UseSqlServer(connString,
    sqlServerOptionsAction: sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
        sqlOptions.CommandTimeout(60);
    })
);


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