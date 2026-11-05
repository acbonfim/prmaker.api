using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using Xunit;

namespace solvace.executionplans.tests;

/// <summary>
/// Tipo da sub-tarefa que espelha a etapa do plano: o <c>taskKind</c> da skill ou a dedução pelo tipo, chave e título —
/// etapa <c>task</c> do Claude não vira mais "Alterar código" (77988: impacto na base, script, fechar o card).
/// </summary>
public class WorkTaskKindFromStepTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-05T12:00:00Z");

    private const string Claude = ExecutionExecutor.Claude;
    private const string User = ExecutionExecutor.User;

    [Theory]
    // Plano de implementação do 77988.
    [InlineData("task", Claude, "impacto-na-base", "Impacto na base", WorkTaskKind.Research)]
    [InlineData("code", Claude, "implementar-edv-solvace", "Implementar no edv-solvace: migracao", WorkTaskKind.Code)]
    [InlineData("validation", Claude, "validar", "Validar build e testes", WorkTaskKind.Validation)]
    [InlineData("task", Claude, "preparar-script-limpar-fotos", "Preparar o script e o chamado de limpeza das fotos", WorkTaskKind.SqlScript)]
    [InlineData("ticket", User, "chamado-limpar-fotos", "Abrir o chamado de limpeza das fotos", WorkTaskKind.Ticket)]
    [InlineData("pr", Claude, "pr-edv-solvace", "Abrir os PRs do edv-solvace (development e qa)", WorkTaskKind.PullRequest)]
    [InlineData("validation", User, "validar-qa", "Validar os criterios de aceite em QA", WorkTaskKind.Validation)]
    [InlineData("task", Claude, "fechar-card", "Fechar o card", WorkTaskKind.DevOpsAction)]
    // Etapas padrão da análise (Bug e US).
    [InlineData("task", Claude, "identificar-card", "Identificar o card", WorkTaskKind.Research)]
    [InlineData("task", Claude, "coletar-dados", "Ler o card", WorkTaskKind.Research)]
    [InlineData("task", Claude, "consultar-base", "Consultar a Base Solvace", WorkTaskKind.Research)]
    [InlineData("task", Claude, "investigar-codigo", "Confirmar no código", WorkTaskKind.Research)]
    [InlineData("task", Claude, "consultar-ambiente", "Consultar dados e ambiente", WorkTaskKind.SqlQuery)]
    [InlineData("task", Claude, "montar-analise", "Montar a análise e os scripts", WorkTaskKind.Research)]
    [InlineData("task", Claude, "publicar", "Publicar na timeline", WorkTaskKind.Notify)]
    [InlineData("question", Claude, "propor-solucoes", "Propor soluções e decidir com você", WorkTaskKind.Decision)]
    [InlineData("task", Claude, "preparar-fechamento", "Preparar o fechamento", WorkTaskKind.DevOpsAction)]
    // Etapa da pessoa sem tipo próprio continua manual.
    [InlineData("task", User, "configurar-permissao", "Ajustar a permissao do perfil no sistema", WorkTaskKind.Manual)]
    public void Step_maps_to_its_task_kind(string kind, string executor, string key, string title, string expected) =>
        Assert.Equal(expected, WorkTaskKind.FromStep(kind, executor, key, title));

    [Theory]
    [InlineData("Levantar o impacto da mudança", WorkTaskKind.Research)]
    [InlineData("Gerar PRMake (descrição + RCA, sem abrir PR)", WorkTaskKind.PrText)]
    [InlineData("Abrir o PR para release-version", WorkTaskKind.PullRequest)]
    [InlineData("Revisar o PR do colega", WorkTaskKind.PrReview)]
    [InlineData("Mover o card para Ready for QA", WorkTaskKind.DevOpsAction)]
    [InlineData("Escrever o script de correção dos dados", WorkTaskKind.SqlScript)]
    [InlineData("Avisar o time no Teams", WorkTaskKind.Notify)]
    [InlineData("Marcar a data da reunião", WorkTaskKind.Task)]
    public void Task_step_with_own_key_is_read_by_the_title(string title, string expected) =>
        Assert.Equal(expected, WorkTaskKind.FromStep(ExecutionStepKind.Task, Claude, "etapa-propria", title));

    [Fact]
    public void Task_step_of_claude_without_hint_is_a_task_not_code() =>
        Assert.Equal(WorkTaskKind.Task, WorkTaskKind.FromStep(ExecutionStepKind.Task, Claude, "x1", "Fazer a coisa"));

    [Fact]
    public void Task_kind_informed_by_the_skill_wins()
    {
        Assert.Equal(WorkTaskKind.SqlQuery, WorkTaskKind.FromStep(ExecutionStepKind.Task, Claude, "impacto-na-base", "Impacto", " SQL-Query "));
        Assert.Throws<DomainException>(() => WorkTaskKind.FromStep(ExecutionStepKind.Task, Claude, "x", "x", "não vale"));
    }

    [Fact]
    public void Legacy_kind_is_what_the_previous_version_gave() =>
        Assert.Equal(WorkTaskKind.Code, WorkTaskKind.LegacyFromStep(ExecutionStepKind.Task, Claude));

    // ---------------------------------------------------------------- item espelho

    private static WorkItem Mirror(params WorkTaskInput[] tasks) =>
        new("77988", WorkItemType.Implementation, "Implementacao 77988", null, WorkItemOrigin.Correction, null, Guid.NewGuid(),
            tasks, Guid.NewGuid(), "Bruno", Now);

    private static string[] ServerKinds(string kind, string executor, string key, string title) =>
        [WorkTaskKind.LegacyFromStep(kind, executor), WorkTaskKind.FromStep(kind, executor, key, title)];

    [Fact]
    public void Pending_task_with_the_server_kind_follows_the_step()
    {
        var item = Mirror(new WorkTaskInput { Title = "Impacto na base", Kind = WorkTaskKind.Code, StepKey = "impacto-na-base" });
        var before = item.Fingerprint;

        var changed = item.SyncStepKind("impacto-na-base", WorkTaskKind.Research,
            ServerKinds(ExecutionStepKind.Task, Claude, "impacto-na-base", "Impacto na base"), Now);

        Assert.True(changed);
        Assert.Equal(WorkTaskKind.Research, item.Tasks.Single().Kind);
        Assert.NotEqual(before, item.Fingerprint);
    }

    [Fact]
    public void Kind_chosen_by_the_person_stays()
    {
        var item = Mirror(new WorkTaskInput { Title = "Impacto na base", Kind = WorkTaskKind.SqlQuery, StepKey = "impacto-na-base" });

        var changed = item.SyncStepKind("impacto-na-base", WorkTaskKind.Research,
            ServerKinds(ExecutionStepKind.Task, Claude, "impacto-na-base", "Impacto na base"), Now);

        Assert.False(changed);
        Assert.Equal(WorkTaskKind.SqlQuery, item.Tasks.Single().Kind);
    }

    [Fact]
    public void Started_task_keeps_its_kind()
    {
        var item = Mirror(new WorkTaskInput { Title = "Fechar o card", Kind = WorkTaskKind.Code, StepKey = "fechar-card" });
        item.SyncStep("fechar-card", ExecutionStatus.Running, null, "Plano", Now);

        var changed = item.SyncStepKind("fechar-card", WorkTaskKind.DevOpsAction,
            ServerKinds(ExecutionStepKind.Task, Claude, "fechar-card", "Fechar o card"), Now);

        Assert.False(changed);
        Assert.Equal(WorkTaskKind.Code, item.Tasks.Single().Kind);
    }
}
