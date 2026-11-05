using solvace.executionplans.domain.Requests;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0085 r5 (1.1/1.4): script vai com chamado. Item com sub-tarefa <c>sql-script</c> e sem nenhuma <c>ticket</c> ganha, logo
/// depois do último script, duas sub-tarefas de chamado: o Claude gera o chamado completo (arquivo <c>ticket</c> do item) e
/// a pessoa abre no sistema de chamados e cola o link. Vale ao criar item (tela, modelo, especialista, plano de correção);
/// o item espelho do plano segue as etapas — lá a skill cria as etapas <c>preparar-script-*</c> + <c>chamado-*</c>.
/// </summary>
public static class WorkItemTicketRule
{
    public const string GenerateTitle = "Gerar o chamado completo (arquivo do chamado)";
    public const string OpenTitle = "Abrir o chamado no sistema de chamados e colar o link";

    /// <summary>
    /// Sub-tarefas com o par do chamado depois do último script (a lista original quando não se aplica: sem script, já
    /// com chamado, ou sem espaço até <see cref="WorkItem.MaxTasks"/>).
    /// </summary>
    /// <param name="ticketSystem"><c>TicketSystem</c> das Skills Configurations (lista separada por vírgula: vale o primeiro).</param>
    public static IReadOnlyList<WorkTaskInput> Apply(IReadOnlyList<WorkTaskInput> tasks, string? ticketSystem)
    {
        if (tasks.Count == 0 || tasks.Count + 2 > WorkItem.MaxTasks) return tasks;
        if (tasks.Any(t => Is(t, WorkTaskKind.Ticket))) return tasks;
        var last = -1;
        for (var i = 0; i < tasks.Count; i++)
            if (Is(tasks[i], WorkTaskKind.SqlScript)) last = i;
        if (last < 0) return tasks;

        var system = FirstSystem(ticketSystem);
        var result = tasks.ToList();
        result.InsertRange(last + 1,
        [
            new WorkTaskInput
            {
                Title = GenerateTitle,
                Kind = WorkTaskKind.Ticket,
                Executor = ExecutionExecutor.Claude,
                Description = "Chamado completo em markdown (contexto, script/arquivos, passo a passo, rollback e como conferir) " +
                              "gravado como arquivo do chamado (kind ticket) — o texto que a pessoa cola no sistema de chamados.",
                Params = Params(system)
            },
            new WorkTaskInput
            {
                Title = OpenTitle,
                Kind = WorkTaskKind.Ticket,
                Executor = ExecutionExecutor.User,
                Description = "Abra o chamado com o arquivo gerado e cole aqui o link (ou o número) do chamado.",
                Params = Params(system)
            }
        ]);
        return result;
    }

    private static Dictionary<string, string> Params(string? system) =>
        system is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["system"] = system };

    private static bool Is(WorkTaskInput t, string kind) =>
        string.Equals(t.Kind?.Trim(), kind, StringComparison.OrdinalIgnoreCase);

    private static string? FirstSystem(string? ticketSystem) =>
        (ticketSystem ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault();
}
