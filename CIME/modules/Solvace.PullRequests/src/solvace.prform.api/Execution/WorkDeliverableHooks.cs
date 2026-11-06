using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.prform.domain.Responses;

namespace solvace.prform.Execution;

/// <summary>
/// 0091: ganchos que gravam a entrega onde a coisa acontece — PR aberto, ação do DevOps, Timeline. Vai para a sub-tarefa
/// informada ou para a em andamento no card (<see cref="IWorkItemDeliverableApplication.RecordAutoAsync"/>). Quem chama
/// diz se foi o Claude (MCP/skill): só aí uma sub-tarefa em andamento de outro tipo recebe a entrega; pela tela, só a de
/// tipo compatível. Nunca falha a ação que já aconteceu.
/// </summary>
public class WorkDeliverableHooks(IWorkItemDeliverableApplication deliverables)
{
    public Task PullRequestAsync(string card, PullRequestGithubResponse pr, long? taskNumber, bool byClaude, ExecutionActor actor, CancellationToken ct) =>
        deliverables.RecordAutoAsync(card, taskNumber, new WorkDeliverableInput
        {
            Kind = WorkDeliverableKind.PullRequest,
            Title = $"PR #{pr.Number} {pr.RepositoryId}: {pr.BranchPrefix}{pr.BranchName} → {pr.TargetBranch}",
            Url = pr.Url,
            Status = pr.IsDraft ? "draft" : "open",
            Repository = pr.RepositoryId,
            Branch = pr.BranchPrefix + pr.BranchName,
            Target = pr.TargetBranch,
            Number = pr.Number,
            Data = Json(new { alreadyExisted = pr.AlreadyExisted, isDraft = pr.IsDraft }),
        }, actor, byClaude, ct);

    /// <summary>Ação do DevOps: <c>log-hours</c> vira horas; as demais, movimento/atualização do card.</summary>
    public Task DevOpsAsync(string card, string action, string message, decimal? hours, string? state, long? taskNumber, bool byClaude,
        ExecutionActor actor, CancellationToken ct)
    {
        var key = (action ?? string.Empty).Trim().ToLowerInvariant();
        var input = key == "log-hours"
            ? new WorkDeliverableInput
            {
                Kind = WorkDeliverableKind.Hours,
                Title = $"{hours?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} h lançadas no card",
                Text = message,
                Data = Json(new { hours }),
            }
            : new WorkDeliverableInput
            {
                Kind = WorkDeliverableKind.DevOpsState,
                Title = string.IsNullOrWhiteSpace(message) ? $"Card no DevOps: {key}" : message,
                Text = message,
                Data = Json(new { action = key, to = state }),
            };
        return deliverables.RecordAutoAsync(card, taskNumber, input, actor, byClaude, ct);
    }

    public Task TimelineAsync(string card, string markdown, long? taskNumber, bool byClaude, ExecutionActor actor, CancellationToken ct) =>
        deliverables.RecordAutoAsync(card, taskNumber, new WorkDeliverableInput
        {
            Kind = WorkDeliverableKind.Timeline,
            Title = "Publicado na Timeline do card",
            Text = markdown,
        }, actor, byClaude, ct);

    /// <summary>Chamada da skill (header <c>X-Execution-Client: skill</c>) ou do executor (credencial dele).</summary>
    public static bool IsClaude(HttpContext? http) =>
        http is not null
        && (string.Equals(http.Request.Headers[solvace.prform.Controllers.ExecutionPlanController.ExecutorHeader].ToString(), "skill", StringComparison.OrdinalIgnoreCase)
            || ExecutorClaims.WorkerIdOf(http.User) is not null);

    /// <summary>Quem fez, pelas claims (sem ir ao banco): ExternalId + nome.</summary>
    public static ExecutionActor ActorOf(HttpContext? http)
    {
        var user = http?.User;
        Guid? id = Guid.TryParse(user?.FindFirst("ExternalId")?.Value, out var g) && g != Guid.Empty ? g : null;
        var name = user?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        return new ExecutionActor(id, string.IsNullOrWhiteSpace(name) ? "Usuário" : name.Trim(), IsClaude(http));
    }

    /// <summary><c>?workItemTask=63</c> (ou <c>#63</c>) da skill.</summary>
    public static long? TaskOf(HttpContext? http) =>
        long.TryParse(http?.Request.Query["workItemTask"].ToString().Trim().TrimStart('#'), out var n) && n > 0 ? n : null;

    private static System.Text.Json.JsonElement Json(object value) => System.Text.Json.JsonSerializer.SerializeToElement(value,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
}
