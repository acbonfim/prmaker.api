using solvace.prform.Execution;
using Xunit;

namespace solvace.prform.tests;

/// <summary>Card 78262: a sub-tarefa pedida no item espelho vira etapa com chave tirada do título, única no plano.</summary>
public class WorkItemAddTaskKeyTests
{
    [Fact]
    public void Key_comes_from_the_title_without_accents()
    {
        var key = WorkItemMcpTools.NewStepKey("Verificar os logs de acesso do otronnier@lindt.com", ["causa-raiz"]);
        Assert.Equal("pedido-verificar-os-logs-de-acesso-do", key);
        Assert.Equal("pedido-revisao-da-sessao", WorkItemMcpTools.NewStepKey("Revisão da sessão", []));
    }

    [Fact]
    public void Key_is_unique_in_the_plan()
    {
        Assert.Equal("pedido-revisar-2", WorkItemMcpTools.NewStepKey("Revisar", ["pedido-revisar"]));
        Assert.Equal("pedido-revisar-3", WorkItemMcpTools.NewStepKey("Revisar", ["pedido-revisar", "pedido-revisar-2"]));
        Assert.Equal("pedido-etapa", WorkItemMcpTools.NewStepKey("!!!", []));
    }
}
