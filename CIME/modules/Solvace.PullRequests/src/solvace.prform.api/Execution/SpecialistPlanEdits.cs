using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;

namespace solvace.prform.Execution;

/// <summary>
/// 0096: o Especialista do caso é o guardião do card — o que a pessoa pede na conversa e muda o plano (etapa, espera,
/// situação publicada, resposta, arquivo, link, evidência, status) ele faz direto, no plano atual, como quem pediu. Este
/// filtro das chamadas MCP cuida do que vale para todas essas ferramentas: com o Claude do tratamento rodando no card, o
/// plano é pausado, a mudança aplicada e o plano retomado; as gravações saem em nome de "Especialista do caso, a pedido de
/// X" (<see cref="PrmakeMcpTools"/> lê <see cref="ItemKey"/>); e a ação vira cartão na conversa (concluída ou falhou).
/// Fora do token do caso não faz nada. Versão nova do plano continua pela proposta aceita por uma pessoa.
/// </summary>
public static class SpecialistPlanEdits
{
    /// <summary>Marca no <c>HttpContext.Items</c>: a chamada é uma mudança do plano pelo especialista.</summary>
    public const string ItemKey = "0096:specialistPlanEdit";

    /// <summary>Ferramentas que mudam o plano e o especialista usa a pedido da pessoa → título do cartão da ação.</summary>
    public static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["prmake_step"] = "Atualizar etapa do plano",
        ["prmake_advance"] = "Concluir etapa do plano",
        ["prmake_unblock"] = "Destravar etapa do plano",
        ["prmake_plan_status"] = "Mudar o status do plano",
        ["prmake_answer"] = "Responder pergunta do plano",
        ["prmake_cancel_questions"] = "Cancelar perguntas do plano",
        ["prmake_handoff"] = "Contexto da espera",
        ["prmake_situation_publish"] = "Publicar a situação na discussion",
        ["prmake_file"] = "Arquivo do plano",
        ["prmake_link"] = "Link na etapa",
        ["prmake_evidence"] = "Evidências do card no plano",
        ["prmake_log"] = "Registro no plano",
        ["prmake_code_ref"] = "Trecho de código citado"
    };

    /// <summary>Não pausam o tratamento: só registram (log, fotografia de código) ou mexem no próprio status.</summary>
    private static readonly HashSet<string> NoPause = new(StringComparer.Ordinal) { "prmake_plan_status", "prmake_log", "prmake_code_ref" };

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, ct) =>
        {
            var name = context.Params?.Name ?? string.Empty;
            if (!Titles.TryGetValue(name, out var title) || context.User is not { } user || CaseClaims.SpecialistOf(user) is null
                || context.Services?.GetService<IHttpContextAccessor>()?.HttpContext is not { } http)
                return await next(context, ct);

            var services = http.RequestServices;
            var specialist = services.GetRequiredService<SpecialistActions>();
            var card = Argument(context.Params, "card")?.Trim().TrimStart('#') ?? CaseClaims.CardOf(user) ?? string.Empty;
            var detail = Detail(context.Params);
            http.Items[ItemKey] = true;
            var resume = (Guid?)null;
            try
            {
                specialist.EnsureCard(card);
                if (!NoPause.Contains(name))
                    resume = await PauseTreatmentAsync(services, specialist, card, Argument(context.Params, "phase"), ct);
                var result = await next(context, ct);
                await specialist.RecordAsync(card, title, detail, result.IsError != true, Text(result), ct);
                return result;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                await specialist.RecordAsync(card, title, detail, false, e.Message, ct);
                throw;
            }
            finally
            {
                http.Items.Remove(ItemKey);
                if (resume is { } planId)
                    await ResumeAsync(services, specialist, planId);
            }
        };

    /// <summary>
    /// Claude do tratamento rodando no card (pedido ativo que não é do especialista nem de item avulso) e plano aberto em
    /// andamento → pausa (devolve o plano para retomar). Sem tratamento rodando, nada a pausar.
    /// </summary>
    private static async Task<Guid?> PauseTreatmentAsync(IServiceProvider services, SpecialistActions specialist, string card, string? phase,
        CancellationToken ct)
    {
        var queue = services.GetRequiredService<IExecutionQueueRepository>();
        if (await queue.GetActiveRequestForCardAsync(card, ct) is not { Status: ExecutionRequestStatus.Running or ExecutionRequestStatus.Claimed })
            return null;
        var plans = services.GetRequiredService<IExecutionPlanApplication>();
        var summaries = await plans.GetByCardAsync(card, ct);
        var p = phase?.Trim().ToLowerInvariant();
        var plan = summaries.Where(s => string.IsNullOrEmpty(p) || s.Phase == p)
            .FirstOrDefault(s => s.Status is not (ExecutionStatus.Completed or ExecutionStatus.Cancelled));
        if (plan is not { Status: ExecutionStatus.Running }) return null;
        await plans.ChangeStatusAsync(plan.Id, new ChangeExecutionPlanStatusRequest
        {
            Status = ExecutionStatus.Paused,
            Reason = $"Especialista ajustando o plano a pedido de {specialist.RequestedBy}"
        }, specialist.Actor()!, ct);
        return plan.Id;
    }

    /// <summary>Retoma o plano pausado aqui (o tratamento segue lendo o plano novo). Best-effort.</summary>
    private static async Task ResumeAsync(IServiceProvider services, SpecialistActions specialist, Guid planId)
    {
        try
        {
            await services.GetRequiredService<IExecutionPlanApplication>().ChangeStatusAsync(planId, new ChangeExecutionPlanStatusRequest
            {
                Status = ExecutionStatus.Running,
                Reason = $"Plano ajustado pelo especialista a pedido de {specialist.RequestedBy}"
            }, specialist.Actor()!, CancellationToken.None);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            services.GetService<ILogger<SpecialistActions>>()?.LogWarning(e, "Plano {Plan} ficou pausado depois do ajuste do especialista", planId);
        }
    }

    private static string? Argument(CallToolRequestParams? p, string key) =>
        p?.Arguments is { } args && args.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Argumentos da chamada (sem o card) para o cartão — curto.</summary>
    private static string? Detail(CallToolRequestParams? p)
    {
        if (p?.Arguments is not { Count: > 0 } args) return null;
        var parts = args.Where(a => a.Key is not ("card" or "phase"))
            .Select(a => $"{a.Key}: {(a.Value.ValueKind == JsonValueKind.String ? a.Value.GetString() : a.Value.GetRawText())}");
        var text = string.Join(" · ", parts);
        return text.Length == 0 ? null : text.Length <= 600 ? text : text[..600] + "…";
    }

    private static string? Text(CallToolResult result)
    {
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text)).Trim();
        return text.Length == 0 ? null : text.Length <= 400 ? text : text[..400] + "…";
    }
}
