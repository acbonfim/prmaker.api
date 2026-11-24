using System.ComponentModel;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using solvace.governance.application;
using solvace.governance.domain;
using solvace.governance.domain.Standards;
using solvace.prform.Governance;
using solvace.prform.Skills;

namespace solvace.prform.Execution;

/// <summary>
/// 0101: padrões de banco e de aplicação pelo MCP — ler os que valem para o card antes de escrever SQL/migração/código
/// e conferir o texto antes de gravar pelo PRMake.
/// </summary>
[McpServerToolType]
public class StandardsMcpTools(IGovernanceStandardsApplication standards, WorkItemToolsService tools, IHttpContextAccessor http)
{
    [McpServerTool(Name = "prmake_standards", ReadOnly = true)]
    [Description("Padroes de banco de dados (nomenclatura, chaves, indices, constraints, views, procedures — wiki da Solvace, SQL Server) e de " +
                 "estrutura de aplicacao (decisoes, stack e onde fica cada coisa no modulo, da engenharia reversa) que valem para o card " +
                 "(repositorios do card; repository restringe). Leia ANTES de escrever script SQL, migracao ou objeto de banco. Legado no dbo " +
                 "mantem os nomes atuais.")]
    public async Task<string> Standards(string card, string? repository = null, CancellationToken ct = default)
    {
        var repos = repository is null ? await tools.CardRepositoriesAsync(card.Trim().TrimStart('#'), ct) : [];
        var md = await standards.ForCardMarkdownAsync(repository, repos, null, ct);
        return md.Length == 0 ? "Nenhum padrao cadastrado vale para este card (ou a governanca esta desligada)." : md;
    }

    [McpServerTool(Name = "prmake_standards_check", ReadOnly = true)]
    [Description("Confere um script SQL, migracao ou codigo (texto) e/ou o caminho de um arquivo contra os padroes. target: sql | migration | " +
                 "code | path. Devolve a decisao (allow/warn/block) e os achados (codigo do padrao, linha, dica). Use ANTES de gravar pelo PRMake " +
                 "(prmake_file, arquivo do item, PR): block = corrija antes; warn = siga citando o aviso; monitor = so registra.")]
    public async Task<string> Check(string card, string? text = null, string target = "sql", string? path = null, string? repository = null,
        CancellationToken ct = default)
    {
        var ctx = http.HttpContext ?? throw new McpException("Sem contexto da requisição.");
        var caller = GovernanceCaller.From(ctx, GovernanceSource.Mcp);
        var cardNumber = int.TryParse(card.Trim().TrimStart('#'), out var n) ? n : (int?)null;
        var repo = repository ?? (await tools.CardRepositoriesAsync(card.Trim().TrimStart('#'), ct)).FirstOrDefault();
        var r = await standards.CheckAsync(new StandardCheckInput
        {
            Text = text, Target = target, Path = path, Repository = repo, Card = cardNumber,
            Source = caller.Source is GovernanceSource.Executor or GovernanceSource.Specialist ? caller.Source : GovernanceSource.Skill
        }, caller.Actor, ct);
        return Format(r);
    }

    public static string Format(StandardCheckResult r)
    {
        if (r.Findings.Count == 0) return "ok — nenhum achado nos padroes.";
        var sb = new StringBuilder();
        var head = r.Decision switch
        {
            GovernanceDecisionKind.Block => "BARRADO pelos padroes — corrija antes de gravar",
            GovernanceDecisionKind.Warn => "Aviso dos padroes — siga e cite no resultado",
            _ => r.WouldDecide == GovernanceDecisionKind.Allow ? "Achados (so monitorados)" : $"Achados (monitorados; no modo aplicar: {r.WouldDecide})"
        };
        sb.AppendLine($"{head}: {r.Findings.Count} achado(s).");
        foreach (var f in r.Findings.Take(30))
            sb.AppendLine($"- {f.Code}{(f.Line is { } l ? $" (linha {l})" : "")} [{f.Effect}]: {f.Hint}{(f.Excerpt.Length > 0 ? $" — `{f.Excerpt}`" : "")}");
        return sb.ToString();
    }
}
