using solvace.executionplans.domain.Requests;

namespace solvace.executionplans.domain.Entities;

/// <summary>
/// 0085 r5 (1.1/1.4): script vai com chamado. Item com sub-tarefa <c>sql-script</c> e sem nenhuma <c>ticket</c> ganha, logo
/// depois do último script, as sub-tarefas do chamado: o Claude gera o chamado completo (arquivo <c>ticket</c> do item), a
/// pessoa abre no sistema de chamados e anexa o link (a sub-tarefa aguarda até o chamado ser resolvido) e — chamado na
/// Bancada — o Claude confere os dados depois do chamado (somente leitura, resultado como evidência), se o item ainda não
/// tiver uma consulta/validação depois do script. Vale ao criar item (tela, modelo, especialista, plano de correção); o item espelho
/// do plano segue as etapas — lá a skill cria as etapas <c>preparar-script-*</c> + <c>chamado-*</c> + <c>validar-chamado-*</c>.
/// </summary>
public static class WorkItemTicketRule
{
    public const string GenerateTitle = "Gerar o chamado completo (arquivo do chamado)";
    public const string OpenTitle = "Abrir o chamado no sistema de chamados e anexar o link";
    public const string ValidateTitle = "Conferir os dados depois do chamado (somente leitura)";

    /// <summary>
    /// Sub-tarefas com o chamado depois do último script (a lista original quando não se aplica: sem script, já com
    /// chamado, ou sem espaço até <see cref="WorkItem.MaxTasks"/>). A conferência só entra se couber e se não houver
    /// consulta/validação depois do script.
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
        var validate = tasks.Count + 3 <= WorkItem.MaxTasks && !tasks.Skip(last + 1).Any(t => Is(t, WorkTaskKind.SqlQuery) || Is(t, WorkTaskKind.Validation));
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
                Description = "Abra o chamado com o arquivo gerado e anexe aqui o link (ou o número) do chamado. A sub-tarefa aguarda " +
                              "o chamado e conclui quando ele for marcado como resolvido.",
                Params = Params(system)
            }
        ]);
        if (validate)
            result.Insert(last + 3, new WorkTaskInput
            {
                Title = ValidateTitle,
                Kind = WorkTaskKind.SqlQuery,
                Executor = ExecutionExecutor.Claude,
                Description = "Com o chamado resolvido, rode a consulta de validação do script (somente leitura) no ambiente do " +
                              "chamado e confirme que os dados foram alterados como o script previa. Grave o resultado (consulta + " +
                              "linhas) como arquivo do item — é a evidência do chamado. Divergência → falhe a sub-tarefa dizendo o que ficou diferente.",
                Params = new Dictionary<string, string> { ["database"] = "", ["how"] = "consulta de validação do script" }
            });
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
