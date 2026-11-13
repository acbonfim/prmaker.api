using System.ComponentModel;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using solvace.executionplans.application;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;

namespace solvace.prform.Execution;

/// <summary>
/// 0080: ferramentas do Especialista do caso — ler o contexto e as mensagens novas, responder, fixar pontos combinados,
/// propor a nova versão do plano e pedir confirmação de ações. Com o token do caso, só estas e as de leitura passam
/// (<see cref="CaseSpecialistMiddleware"/>).
/// </summary>
[McpServerToolType]
public class CaseChatMcpTools(CaseChatApplication chat, IHttpContextAccessor http)
{
    // Acentos como estão (menos tokens que \u00E3) — o texto vai para o modelo, não para HTML.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [McpServerTool(Name = "prmake_case_context", ReadOnly = true)]
    [Description("Contexto compacto do caso (Especialista do caso): planos e versoes (com o motivo de cancelamento), decisoes, perguntas abertas, " +
                 "divergencia, PRs, pontos combinados, propostas, acoes e as ultimas mensagens da conversa. Comece por aqui.")]
    public Task<string> Context(string card, CancellationToken ct = default) =>
        Safe(async () => await chat.ContextAsync(Card(card), ct));

    [McpServerTool(Name = "prmake_case_messages", ReadOnly = true)]
    [Description("Mensagens novas da conversa do caso (depois da ultima que voce leu; 'after' = a partir do numero informado). Marca como lidas. " +
                 "Mensagem de voz vem com a transcricao; arquivos aparecem como 'arquivo #n' (baixe com prmake-case.sh file <card> <n>).")]
    public Task<string> Messages(string card, int? after = null, CancellationToken ct = default) =>
        Safe(async () =>
        {
            var messages = await chat.MessagesForSpecialistAsync(Card(card), after, Actor(), ct);
            if (messages.Count == 0) return "Nenhuma mensagem nova.";
            return Serialize(messages.Select(m => new
            {
                m.Number,
                who = m.AuthorKind == CaseAuthorKind.Person ? m.AuthorName : m.AuthorKind == CaseAuthorKind.Specialist ? "Especialista" : "PRMake",
                m.Kind,
                text = m.Kind == CaseMessageKind.Voice ? $"[voz] {m.Transcript ?? "(sem transcricao — peca um resumo em texto)"}" : m.Text,
                peopleOnly = m.PeopleOnly ? true : (bool?)null,
                deep = m.Deep ? true : (bool?)null,
                replyTo = m.ReplyToNumber,
                files = m.Files.Count == 0 ? null : m.Files.Select(f => $"arquivo #{f.Number} {f.Name} ({f.Kind}, {f.Size / 1024} KB)").ToList(),
                at = m.CreatedAt
            }));
        });

    [McpServerTool(Name = "prmake_case_reply")]
    [Description("Responde na conversa do caso (markdown: trechos de codigo com caminho:linha, links, tabelas). 'steps' = o que voce fez para " +
                 "responder (frases curtas: 'Li o print', 'Consultei X na QA (so leitura)'); 'files' = numeros dos arquivos que voce subiu " +
                 "(prmake-case.sh upload) e entram nesta mensagem.")]
    public Task<string> Reply(string card, string text, string[]? steps = null, int? replyTo = null, int[]? files = null, CancellationToken ct = default) =>
        Safe(async () =>
        {
            var m = await chat.ReplyAsync(Card(card), new CaseReplyRequest { Text = text, Steps = steps?.ToList(), ReplyTo = replyTo, Files = files?.ToList() },
                Actor(), null, ct);
            return $"Mensagem {m.Number} enviada.";
        });

    [McpServerTool(Name = "prmake_case_point")]
    [Description("Fixa (ou atualiza, com id) um ponto combinado na conversa: status agreed (combinado), discussing (em discussao) ou dropped " +
                 "(descartado). Os pontos sao a base da proposta de mudanca do plano.")]
    public Task<string> Point(string card, string? text = null, string? status = null, int? messageNumber = null, string? id = null, CancellationToken ct = default) =>
        Safe(async () =>
        {
            var request = new CasePointRequest { Text = text, Status = status, MessageNumber = messageNumber };
            var p = Guid.TryParse(id, out var pid)
                ? await chat.UpdatePointAsync(Card(card), pid, request, Actor(), ct)
                : await chat.AddPointAsync(Card(card), request, Actor(), ct);
            return $"Ponto {p.Id} ({p.Status}): {p.Text}";
        });

    [McpServerTool(Name = "prmake_case_propose")]
    [Description("Propoe mudar a SOLUCAO do plano atual (vira um cartao na conversa; nada muda ate alguem com permissao aceitar; aceita, o " +
                 "tratamento ajusta o MESMO plano — nao existe versao nova do plano). Outra entrega/outra solucao a parte = item de trabalho novo " +
                 "(prmake_work_item_create), nao proposta. " +
                 "0096: ajuste do plano atual (texto de etapa, espera, situacao, concluir etapa) nao e proposta — faca direto (prmake_step, prmake_handoff...). " +
                 "changes: [{kind: add|change|remove|decision|keep, title, detail?, fromMessage?}]; phase: correction (padrao) ou analysis; " +
                 "message: o texto do cartao ('Chegamos a uma conclusao...').")]
    public Task<string> Propose(string card, string title, CaseProposalChange[] changes, string? summary = null, string? phase = null, string? message = null,
        CancellationToken ct = default) =>
        Safe(async () =>
        {
            var p = await chat.ProposeAsync(Card(card), new CaseProposalRequest
            {
                Title = title, Summary = summary, Phase = phase, Changes = changes.ToList(), Message = message
            }, Actor(), ct);
            return $"Proposta #{p.Number} criada ({p.Changes.Count} mudancas) — aguardando a decisao de uma pessoa.";
        });

    [McpServerTool(Name = "prmake_case_action")]
    [Description("Pede confirmacao para uma acao com efeito fora da conversa quando o pedido e ambiguo ou a acao nao tem ferramenta do PRMake " +
                 "(ex.: subir arquivo num ambiente). Acao pedida explicitamente (Timeline, horas/estado do card, PR, item de trabalho) roda direto " +
                 "pela ferramenta dela, que ja registra o cartao. So execute depois da mensagem 'confirmou a acao #n'; ao terminar, prmake_case_action_done.")]
    public Task<string> Action(string card, string title, string? detail = null, string? message = null, CancellationToken ct = default) =>
        Safe(async () =>
        {
            var a = await chat.RequestActionAsync(Card(card), new CaseActionRequest { Title = title, Detail = detail, Message = message }, Actor(), ct);
            return $"Acao #{a.Number} (id {a.Id}) aguardando confirmacao — encerre a vez; a confirmacao chega como mensagem.";
        });

    [McpServerTool(Name = "prmake_case_action_done")]
    [Description("Registra o resultado de uma acao confirmada (ok=false quando falhou).")]
    public Task<string> ActionDone(string card, string id, bool ok = true, string? result = null, CancellationToken ct = default) =>
        Safe(async () =>
        {
            if (!Guid.TryParse(id, out var aid)) throw new DomainException("Informe o id da acao.");
            var a = await chat.FinishActionAsync(Card(card), aid, new CaseActionFinishRequest { Ok = ok, Result = result }, Actor(), ct);
            return $"Acao #{a.Number}: {a.Status}.";
        });

    [McpServerTool(Name = "prmake_case_file")]
    [Description("Grava um arquivo de TEXTO gerado por voce na conversa (script .sql, texto de chamado .md, .csv). Devolve o numero para usar em " +
                 "prmake_case_reply(files). Imagens: prmake-case.sh upload <card> <arquivo>.")]
    public Task<string> File(string card, string name, string content, CancellationToken ct = default) =>
        Safe(async () =>
        {
            var type = Path.GetExtension(name).ToLowerInvariant() switch
            {
                ".md" => "text/markdown",
                ".csv" => "text/csv",
                ".json" => "application/json",
                ".sql" => "application/sql",
                ".html" => "text/html",
                _ => "text/plain"
            };
            var f = await chat.UploadAsync(Card(card), new ExecutionArtifactUpload(name, null, null, null, type, Encoding.UTF8.GetBytes(content)), Actor(), ct);
            return $"arquivo #{f.Number} {f.Name}";
        });

    private string Card(string card)
    {
        var c = CaseChat.NormalizeCard(card);
        if (CaseClaims.CardOf(User) is { } own && !string.Equals(own, c, StringComparison.Ordinal))
            throw new ExecutionForbiddenException($"Este token e do caso {own}.");
        return c;
    }

    private ClaimsPrincipal User => http.HttpContext?.User ?? new ClaimsPrincipal();

    private CaseActor Actor() =>
        CaseClaims.SpecialistOf(User)
        ?? new CaseActor(Guid.TryParse(User.FindFirst("ExternalId")?.Value, out var id) ? id : null, User.FindFirst(ClaimTypes.Name)?.Value ?? "Usuario");

    private static async Task<string> Safe(Func<Task<string>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception e) when (e is DomainException or ExecutionPlanNotFoundException or ExecutionForbiddenException or ExecutionPlanConcurrencyException
                                       or CaseNumberConflictException)
        {
            throw new McpException(e.Message, e);
        }
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}
