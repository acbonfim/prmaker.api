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

/// <summary>
/// 0085 r4: tipos de item de trabalho configurados (<c>WorkItemTypes</c>) e o tipo de cada modelo (<c>WorkItemTemplates[].type</c>)
/// — implementado no host. Sem implementação (testes), vale só o formato do tipo.
/// </summary>
public interface IWorkItemTypeSource
{
    Task<WorkItemTypeSettings> GetAsync(CancellationToken cancellationToken);
}

/// <param name="Keys">Tipos aceitos ao criar (os configurados; vazio = os semeados).</param>
/// <param name="TemplateTypes">Modelo (key) → tipo do item.</param>
public record WorkItemTypeSettings(IReadOnlyList<string> Keys, IReadOnlyDictionary<string, string> TemplateTypes);

/// <summary>Tipo de item fora do <c>WorkItemTypes</c> → 400 com as opções.</summary>
public class WorkItemTypeInvalidException(string type, IReadOnlyList<string> options)
    : solvace.executionplans.domain.Entities.DomainException($"Tipo de item inválido: '{type}'. Use um destes: {string.Join(", ", options)}.")
{
    public string Type { get; } = type;
    public IReadOnlyList<string> Options { get; } = options;
}
