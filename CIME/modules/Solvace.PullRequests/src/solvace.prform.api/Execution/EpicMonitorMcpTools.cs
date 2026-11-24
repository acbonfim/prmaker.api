using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;

namespace solvace.prform.Execution;

/// <summary>
/// 0099 (ajustes 2): acompanhamento das entregas do épico pelo MCP — o mesmo do <c>GET Epics/{n}/monitor</c> em texto
/// (PO / gestor de problemas: o que já tem PR mesclado e em que ambiente está, sem git/AWS na máquina). Leitura.
/// </summary>
[McpServerToolType]
public class EpicMonitorMcpTools(EpicMonitorService monitor, IEpicApplication epics, SpecialistActions specialist)
{
    [McpServerTool(Name = "prmake_epic_monitor", ReadOnly = true)]
    [Description("Acompanhamento das entregas do epico: por card, estado/coluna/responsavel no DevOps, PRs registrados no PRMake (status " +
                 "open|merged|closed e branch de destino, sem consultar o GitHub agora) e ambientes do retrato da AWS (implantado/aguardando/" +
                 "falhou, com a data do retrato). Resumo no topo (cards, com PR mesclado, em producao). Use para 'o que ja subiu?', " +
                 "'esta em producao?', 'quais PRs faltam?' — sem git, gh nem CLI da AWS.")]
    public async Task<string> Monitor([Description("Numero do epico (#n) ou a chave E<n>")] string epic, CancellationToken ct = default)
    {
        try
        {
            var number = EpicMcpTools.ParseNumber(epic);
            // Token do caso num épico: só o próprio (o REST faz o mesmo); num card, ler qualquer épico vale.
            if (EpicKey.TryParse(specialist.OwnCard, out var ownEpic) && ownEpic != number)
                throw new McpException("O Especialista do caso so acompanha o epico do caso dele.");
            var info = await epics.GetAsync(number, ct);
            return EpicMonitorMapping.Text(number, info.Title, await monitor.GetAsync(number, ct));
        }
        catch (Exception e) when (e is ExecutionPlanNotFoundException or ExecutionForbiddenException or DomainException)
        {
            throw new McpException(e.Message, e);
        }
    }
}
