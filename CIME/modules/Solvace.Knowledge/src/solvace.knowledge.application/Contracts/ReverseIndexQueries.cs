using solvace.knowledge.domain.Entities;

namespace solvace.knowledge.application.Contracts;

/// <summary>Quanto do texto do item a consulta traz do banco (0073).</summary>
public enum ReverseBodyMode
{
    /// <summary>Sem texto (só cabeça: ID, tipo, título, listas).</summary>
    None,
    /// <summary>Só o começo (o suficiente para o trecho do resultado — <see cref="ReverseSearch.Snippet"/>).</summary>
    Preview,
    /// <summary>O texto inteiro.</summary>
    Full,
    /// <summary>O texto inteiro e as evidências (o item completo, como na tela do item).</summary>
    Complete
}

/// <summary>Filtro das consultas leves ao índice da ER (0073): tudo é feito no banco, nada vai para o cache da instância.</summary>
public sealed class ReverseIndexQuery
{
    public IReadOnlyCollection<string>? Modules { get; init; }
    public IReadOnlyCollection<string>? Kinds { get; init; }
    public IReadOnlyCollection<string>? ExcludeKinds { get; init; }
    public string? DocType { get; init; }
    public string? NotDocType { get; init; }
    public bool IncludeRemoved { get; init; } = true;
    public ReverseBodyMode Body { get; init; } = ReverseBodyMode.None;
    public int? Limit { get; init; }
}

/// <summary>
/// Seleção dos candidatos da busca (0073): o banco devolve os itens cujo texto contém algum dos termos (ou sinônimos), os de ID
/// exato e os cujo título/tags trazem um nome do glossário citado na consulta. O ranking continua em <see cref="ReverseSearch"/>.
/// </summary>
public sealed class ReverseSearchCandidates
{
    /// <summary>
    /// Um grupo por termo da consulta: o radical e os sinônimos dele (já normalizados). O item entra se o texto normalizado contém
    /// algum nome de algum grupo; o banco ordena os candidatos por uma pontuação aproximada, com os mesmos pesos do ranking.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> TermGroups { get; init; } = [];
    /// <summary>Nomes do glossário (palavras separadas por espaço, com espaço nas pontas) que o título/tags podem trazer.</summary>
    public IReadOnlyList<string> Phrases { get; init; } = [];
    /// <summary>IDs exatos citados na consulta; <c>Module</c> nulo = qualquer módulo.</summary>
    public IReadOnlyList<(string? Module, string Id)> ExactIds { get; init; } = [];
    public IReadOnlyCollection<string>? Modules { get; init; }
    public IReadOnlyCollection<string>? Kinds { get; init; }
    public string? DocType { get; init; }
    public bool IncludeRemoved { get; init; }
    /// <summary>Teto de candidatos (os que casam mais termos primeiro).</summary>
    public int Max { get; init; } = 1000;
}
