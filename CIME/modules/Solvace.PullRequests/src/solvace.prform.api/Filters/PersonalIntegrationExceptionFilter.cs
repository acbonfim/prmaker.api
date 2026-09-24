using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using solvace.prform.application.UserIntegrations;

namespace solvace.prform.api.Filters;

/// <summary>
/// Função que depende de plugin de uso pessoal não configurado pelo usuário →
/// 403 { error, code: "PERSONAL_INTEGRATION_REQUIRED", plugins } (o front abre "Minhas integrações").
/// </summary>
public sealed class PersonalIntegrationExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not PersonalIntegrationRequiredException e)
            return;

        context.Result = new ObjectResult(new
        {
            error = e.Message,
            code = PersonalIntegrationRequiredException.Code,
            plugins = e.Plugins
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
        context.ExceptionHandled = true;
    }
}
