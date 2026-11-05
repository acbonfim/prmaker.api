namespace solvace.executionplans.domain.Entities;

/// <summary>
/// Itens de trabalho a partir da análise (card 77988): a análise terminou sem plano de implementação/correção — a pessoa
/// respondeu "só a análise por enquanto" — e não havia como pedir os itens depois. O pedido vai para a fila e a fase de
/// entrega monta o plano (que o PRMake transforma no item de trabalho); sem iniciar, o plano fica pausado até o Executar.
/// </summary>
public static class AnalysisDelivery
{
    /// <summary>Etapa da skill em que as soluções são propostas ao usuário (fim da análise).</summary>
    public const string ProposeSolutionsStep = "propor-solucoes";

    /// <summary>
    /// A análise já decidiu a solução: <c>propor-solucoes</c> concluída ou todas as perguntas dela respondidas — a próxima
    /// execução do card já é a fase de entrega.
    /// </summary>
    public static bool IsAnswered(ExecutionPlan plan, IEnumerable<ExecutionQuestion> questions)
    {
        if (ProposalCompleted(plan))
            return true;
        var proposal = questions
            .Where(q => q.StepKey == ProposeSolutionsStep && q.Status != ExecutionQuestionStatus.Cancelled)
            .ToList();
        return proposal.Count > 0 && proposal.All(q => q.Status == ExecutionQuestionStatus.Answered);
    }

    public static bool ProposalCompleted(ExecutionPlan plan) =>
        plan.Steps.Any(s => s.Key == ProposeSolutionsStep && s.Status == ExecutionStatus.Completed);

    /// <summary>Por que o card não pode pedir os itens da análise agora; null = pode.</summary>
    public static string? Refusal(ExecutionPlan? current, IEnumerable<ExecutionQuestion> questions)
    {
        if (current is null)
            return "O card ainda não tem análise — analise primeiro.";
        if (current.Phase == ExecutionPhase.Correction)
            return current.Status == ExecutionStatus.Cancelled
                ? $"O {PlanTerms.DeliveryPlan(current.Kind)} foi cancelado — peça uma nova versão pelo Especialista do caso."
                : $"O card já tem {PlanTerms.DeliveryPlan(current.Kind)} — o item de trabalho dele está na Bancada.";
        if (current.Status == ExecutionStatus.Cancelled)
            return "A análise foi cancelada — analise de novo.";
        if (!IsAnswered(current, questions))
            return "A análise ainda não decidiu a solução — responda as perguntas de \"Propor a solução\" primeiro.";
        return null;
    }

    /// <summary>Instrução que vai no prompt do executor (observação do pedido).</summary>
    public static string Instruction(string? planKind, bool start, string? note)
    {
        var delivery = WorkItem.IsUserStoryPlan(planKind) ? "implementacao" : "correcao";
        var text = $"Criar os itens de trabalho a partir da analise (botao \"Criar itens da analise\" do PRMake): monte agora o plano de {delivery} " +
                   "(passo 7 da skill) com as respostas da analise — o PRMake transforma o plano no item de trabalho; entregas independentes " +
                   "viram itens proprios. Este pedido vale por cima de uma resposta anterior como \"so a analise por enquanto\" ou \"nao seguir agora\". ";
        text += start
            ? "Depois de criar, siga executando o plano (passo 8)."
            : $"NAO execute nenhuma etapa: criado o plano (e os itens separados, se houver), pause-o com prmake_plan_status(card, \"paused\", " +
              $"reason: \"Criado sem iniciar — use Executar no item da Bancada\", phase: \"correction\") (script: status <card> paused \"...\") e encerre a vez. " +
              "A pessoa inicia pelo Executar do item na Bancada.";
        if (!string.IsNullOrWhiteSpace(note))
            text += $"\nO que a pessoa acrescentou: {note.Trim()}";
        return text.Length <= ExecutionRequest.MaxNoteLength ? text : text[..ExecutionRequest.MaxNoteLength];
    }
}
