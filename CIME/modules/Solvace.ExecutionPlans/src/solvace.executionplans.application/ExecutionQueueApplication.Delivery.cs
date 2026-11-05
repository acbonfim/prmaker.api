using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;
using solvace.executionplans.domain.Requests;
using solvace.executionplans.domain.Responses;

namespace solvace.executionplans.application;

public partial class ExecutionQueueApplication
{
    public async Task<ExecutionRequestResponse> RequestDeliveryAsync(string cardNumber, bool start, string? note, ExecutionActor actor,
        CancellationToken cancellationToken)
    {
        var card = (cardNumber ?? string.Empty).Trim();
        if (card.Length == 0)
            throw new DomainException("Informe o número do card.");
        var plan = await _plans.GetCurrentPlanIdAsync(card, cancellationToken) is { } planId
            ? await _plans.GetPlanWithStepsAsync(planId, cancellationToken)
            : null;
        List<ExecutionQuestion> questions = plan is null ? [] : await _plans.GetQuestionsAsync(plan.Id, cancellationToken);
        if (AnalysisDelivery.Refusal(plan, questions) is { } refusal)
            throw new DomainException(refusal);
        // O pedido ativo do card seria devolvido sem a instrução (CreateAsync não cria um segundo).
        if (await _queue.GetActiveRequestForCardAsync(card, cancellationToken) is not null)
            throw new DomainException("O Claude já tem um pedido neste card — espere terminar e peça de novo.");

        return await CreateAsync(new CreateExecutionRequestRequest { CardNumber = card, Note = AnalysisDelivery.Instruction(plan!.Kind, start, note) },
            actor, ExecutionRequestSource.Delivery, cancellationToken);
    }
}
