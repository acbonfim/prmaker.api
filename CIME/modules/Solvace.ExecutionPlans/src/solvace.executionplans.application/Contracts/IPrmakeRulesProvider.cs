namespace solvace.executionplans.application.Contracts;

/// <summary>
/// 0085: "Regras do PRMake" calculadas para o card (fluxo pela área, branch principal, nome de branch por destino,
/// padrões, regras fixas) — implementado no host (Skills Configurations + card no DevOps). O contexto do Especialista do
/// caso inclui o resumo para ele não inventar nome de branch.
/// </summary>
public interface IPrmakeRulesProvider
{
    /// <summary>Markdown das regras; <paramref name="summary"/> = versão curta (contexto do caso). Null = indisponível.</summary>
    Task<string?> RulesMarkdownAsync(string cardNumber, bool summary, CancellationToken cancellationToken);

    /// <summary>O card é User Story no DevOps? Null = não deu para ler o card.</summary>
    Task<bool?> IsUserStoryAsync(string cardNumber, CancellationToken cancellationToken);
}
