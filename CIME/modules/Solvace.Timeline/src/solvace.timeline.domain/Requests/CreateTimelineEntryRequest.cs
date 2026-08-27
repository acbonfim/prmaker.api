namespace solvace.timeline.domain.Requests;

public class CreateTimelineEntryRequest
{
    /// <summary>Número do card ao qual o registro será vinculado.</summary>
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Descrição do que foi feito.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Nome de quem realizou o registro. Obrigatório apenas para chamadas externas
    /// (sem usuário logado). Quando há usuário logado, o nome é obtido da base e este campo é ignorado.
    /// </summary>
    public string? UserName { get; set; }
}
