namespace solvace.knowledge.domain.Entities;

/// <summary>Ambiente do Knowledge Center de onde a cópia veio (0033: hoje dev; trocar para prod é configuração).</summary>
public static class KnowledgeEnvironment
{
    public const string Dev = "dev";
    public const string Prod = "prod";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { Dev, Prod };

    public static string Normalize(string? environment)
    {
        var value = (environment ?? string.Empty).Trim().ToLowerInvariant();
        return All.Contains(value) ? value : throw new DomainException($"Ambiente do Knowledge Center inválido: '{environment}' (use dev ou prod).");
    }
}
