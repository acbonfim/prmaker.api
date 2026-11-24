using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using solvace.executionplans.domain.Entities;

namespace solvace.prform.api.Filters;

/// <summary>
/// 0099: a chave de épico (<c>E123</c>) circula onde antes só havia card do DevOps (a Bancada reaproveitada na Mesa de
/// gestão). Nos controllers que chamam o DevOps com o card da rota (<c>Azure/card/{id}…</c>: card, card full, horas,
/// estados, ações, comentário, spec), responde 404 <c>{ code: "epic", error }</c> limpo em vez de deixar o DevOps falhar (500).
/// </summary>
public sealed class EpicKeyCardFilter : IActionFilter
{
    /// <summary>Controller → nomes do parâmetro de rota que é o card do DevOps.</summary>
    private static readonly Dictionary<string, string[]> CardRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Azure"] = ["id", "card", "cardNumber"],
    };

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action
            || !CardRoutes.TryGetValue(action.ControllerName, out var names))
            return;
        foreach (var name in names)
        {
            if (context.RouteData.Values.TryGetValue(name, out var value) && value?.ToString() is { } card && EpicKey.IsEpic(card))
            {
                context.Result = new NotFoundObjectResult(new
                {
                    code = "epic",
                    error = $"{card.Trim()} é um épico da Mesa de gestão, não um card do DevOps."
                });
                return;
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
