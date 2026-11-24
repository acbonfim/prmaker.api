using System.ComponentModel;
using ModelContextProtocol.Server;
using solvace.prform.AwsDeploy;

namespace solvace.prform.Execution;

public partial class PrmakeMcpTools
{
    /// <summary>
    /// 0100: o que a AWS diz do card (último retrato coletado pela máquina) — o serviço vem do escopo da requisição para não
    /// mexer no construtor das ferramentas.
    /// </summary>
    [McpServerTool(Name = "prmake_aws_status", ReadOnly = true)]
    [Description("Deploy do card na AWS (ultimo retrato da aba AWS): por ambiente (DEV, QA, Sandboxefeso, SandboxRT, Producao) se o nosso codigo " +
                 "ja esta la, e as pipelines (status, ultima execucao, commit implantado) + quando e por quem foi coletado. Consulte ANTES de " +
                 "dizer que algo esta (ou nao) em um ambiente. Sem retrato: peca a coleta pela aba AWS (Atualizar) ou rode " +
                 "bash ~/.claude/skills/aws-deploy/scripts/aws.sh collect <card>.")]
    public Task<string> AwsStatus(
        [Description("Numero do card")] string card,
        CancellationToken ct = default) => Safe(async () =>
    {
        var aws = http.HttpContext?.RequestServices.GetRequiredService<AwsDeployService>()
                  ?? throw new InvalidOperationException("Serviço da aba AWS indisponível nesta requisição.");
        return await aws.StatusTextAsync(card, ct);
    });
}
