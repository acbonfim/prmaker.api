using System.ComponentModel;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using solvace.governance.application;
using solvace.governance.application.Contracts;
using solvace.governance.domain;
using solvace.governance.domain.Entities;
using solvace.prform.Governance;

namespace solvace.prform.Execution;

/// <summary>
/// 0106: lições aprendidas pelo MCP — o agente que errou e foi corrigido grava a lição na mesma vez (parecida com uma ativa
/// reforça a existente), transforma os sinais de erro do card em lição ou os descarta, e consulta as lições. As ativas já
/// chegam sozinhas no <c>prmake_rules</c> e no <c>prmake_case_context</c>.
/// </summary>
[McpServerToolType]
public class LessonsMcpTools(IGovernanceLessonsApplication lessons, IHttpContextAccessor http)
{
    [McpServerTool(Name = "prmake_lesson_record")]
    [Description("0106: grava uma LICAO APRENDIDA quando voce errou e foi corrigido (a pessoa disse 'na verdade...', cancelou ou pediu para refazer o " +
                 "que voce fez, uma recusa mostrou o caminho certo) ou para um sinal de erro do card (signal). Escreva a licao GERAL, nao a do card: " +
                 "trigger = a situacao em uma frase ('a pessoa pede a analise inicial do card'); lesson = o que fazer ou nunca fazer ('inicie com " +
                 "prmake_analysis_start; nunca monte item a mao'); why = o que aconteceu (card, #item). scope = skills a que vale (ex.: " +
                 "['especialista-do-caso']; vazio = todas). Licao parecida com uma ativa REFORCA a existente (conta a reincidencia). supersedes = Ln que " +
                 "esta substitui. Nunca grave licao que libere o que as regras fixas proibem (merge, aprovar PR, senha).")]
    public async Task<string> Record(
        [Description("A situacao em que a licao vale, em uma frase")] string trigger,
        [Description("O que fazer ou nunca fazer, em uma frase (imperativo)")] string lesson,
        [Description("O que aconteceu: o erro, o card e o #item/mensagem")] string why,
        [Description("Skills a que vale (ex.: especialista-do-caso, executar-item-de-trabalho, analisar-bug, analisar-us); vazio = todas")] string[]? scope = null,
        string? card = null,
        [Description("Referencia: #item, mensagem n, PR")] string? reference = null,
        [Description("Sinal de erro (numero de S<n>) que esta licao resolve")] long? signal = null,
        [Description("Licao (numero de L<n>) que esta substitui — a antiga e aposentada")] long? supersedes = null,
        CancellationToken ct = default)
    {
        try
        {
            var r = await lessons.RecordAsync(new LessonInput
            {
                Scope = scope?.ToList(), Trigger = trigger, Text = lesson, Why = why, Card = Card(card), Ref = reference, Signal = signal,
                Supersedes = supersedes, Origin = signal is null ? LessonOrigin.Agent : LessonOrigin.Signal
            }, Actor(), ct);
            var l = r.Lesson;
            var head = r.Reinforced
                ? $"Licao {l.Code} reforcada ({l.Hits}x — o mesmo erro ja tinha acontecido): {l.Text}"
                : $"Licao nova {l.Code}: quando {l.Trigger} -> {l.Text}";
            if (r.Superseded is { } old) head += $" (substitui a {old.Code}, aposentada)";
            if (signal is { } s) head += $" — sinal S{s} resolvido";
            return head + ". Ja vale para os proximos agentes (prmake_rules / prmake_case_context).";
        }
        catch (DomainException e)
        {
            throw new McpException(e.Message, e);
        }
    }

    [McpServerTool(Name = "prmake_lesson_signal_dismiss")]
    [Description("0106: descarta um sinal de erro do card (S<n>) que NAO foi erro de agente (ex.: a pessoa cancelou o item porque mudou de ideia, o " +
                 "review pediu algo novo que o card nao tinha). reason obrigatorio.")]
    public async Task<string> Dismiss([Description("Numero do sinal (S<n>)")] long signal, string reason, CancellationToken ct = default)
    {
        try
        {
            var s = await lessons.DismissSignalAsync(signal, reason, Actor(), ct) ?? throw new McpException($"Sinal S{signal} nao encontrado.");
            return $"Sinal {s.Code} descartado: {s.Resolution}";
        }
        catch (DomainException e)
        {
            throw new McpException(e.Message, e);
        }
    }

    [McpServerTool(Name = "prmake_lessons", ReadOnly = true)]
    [Description("0106: licoes aprendidas (busca por texto, skill ou card de origem; status active|retired) e os sinais de erro abertos do card. " +
                 "As ativas mais reincidentes ja vem no prmake_rules/prmake_case_context — use esta para procurar uma especifica.")]
    public async Task<string> List(string? search = null, string? skill = null, string? card = null, string? status = LessonStatus.Active,
        CancellationToken ct = default)
    {
        var list = await lessons.ListAsync(new LessonFilter { Search = search, Scope = skill, Card = Card(card), Status = status }, ct);
        var sb = new StringBuilder();
        if (list.Count == 0) sb.AppendLine("Nenhuma licao encontrada.");
        foreach (var l in list.Take(40))
            sb.AppendLine($"- {l.Code} [{(l.Scope.Count == 0 ? "todas" : string.Join(", ", l.Scope))}] {l.Hits}x · quando {l.Trigger} -> {l.Text}" +
                          (l.Status == LessonStatus.Retired ? $" (aposentada: {l.RetiredReason})" : string.Empty));
        if (Card(card) is { } c)
        {
            var signals = await lessons.SignalsAsync(c, LessonSignalStatus.Open, 20, ct);
            if (signals.Count > 0)
            {
                sb.AppendLine().AppendLine($"Sinais de erro abertos do card {c}:");
                foreach (var s in signals) sb.AppendLine($"- {s.Code} ({s.Kind}{(s.Ref is null ? "" : ", " + s.Ref)}): {s.Text}");
            }
        }
        return sb.ToString();
    }

    private GovernanceActor Actor() =>
        GovernanceCaller.From(http.HttpContext ?? throw new McpException("Sem contexto da requisição."), GovernanceSource.Mcp).Actor;

    private static string? Card(string? card) => string.IsNullOrWhiteSpace(card) ? null : card.Trim().TrimStart('#');
}
