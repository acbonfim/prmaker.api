using solvace.azure.application.Contract;
using solvace.executionplans.application.Contracts;
using solvace.executionplans.domain.Entities;

namespace solvace.prform.Execution;

/// <summary>
/// Binários do executor (<c>prmake-agent</c>, 0039) compilados na imagem (<c>/app/agent/&lt;rid&gt;/</c>) e a versão
/// publicada (<c>/app/agent/version.txt</c>). Fora da imagem (dev), <c>ExecutionAgent:Path</c> aponta para outra pasta.
/// </summary>
public class ExecutionAgentCatalog : IExecutionAgentInfo
{
    public static readonly IReadOnlyList<string> Rids = ["osx-arm64", "osx-x64", "win-x64", "linux-x64", "linux-arm64"];

    private readonly string _root;

    public ExecutionAgentCatalog(IConfiguration configuration)
    {
        _root = configuration["ExecutionAgent:Path"] is { Length: > 0 } path ? path : Path.Combine(AppContext.BaseDirectory, "agent");
        var versionFile = Path.Combine(_root, "version.txt");
        LatestVersion = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : null;
    }

    public string? LatestVersion { get; }

    /// <summary>Caminho do binário do RID (null = não publicado nesta imagem).</summary>
    public string? BinaryPath(string rid)
    {
        if (!Rids.Contains(rid)) return null;
        var name = rid.StartsWith("win", StringComparison.Ordinal) ? "prmake-agent.exe" : "prmake-agent";
        var path = Path.Combine(_root, rid, name);
        return File.Exists(path) ? path : null;
    }

    public IEnumerable<string> Available() => Rids.Where(r => BinaryPath(r) is not null);
}

/// <summary>
/// Regra automática da fila (0039): consulta WIQL no Azure DevOps com a configuração efetiva do dono (o long-poll do
/// executor dele roda com as claims dele, então a integração pessoal do Azure vale).
/// </summary>
public class ExecutionWorkItemSource(IAzureService azure) : IExecutionWorkItemSource
{
    public async Task<IReadOnlyList<string>> FindCardsAsync(ExecutionUserSettings settings, int max, CancellationToken cancellationToken)
    {
        var ids = await azure.QueryWorkItemIdsAsync(BuildWiql(settings), max, cancellationToken);
        return ids.Select(i => i.ToString()).ToList();
    }

    public static string BuildWiql(ExecutionUserSettings settings)
    {
        static string Quote(string v) => "'" + v.Replace("'", "''") + "'";
        static string In(IEnumerable<string> values) => string.Join(", ", values.Select(Quote));

        var where = new List<string> { "[System.TeamProject] = @project" };
        if (settings.AutoWorkItemTypes.Count > 0) where.Add($"[System.WorkItemType] IN ({In(settings.AutoWorkItemTypes)})");
        if (settings.AutoStates.Count > 0) where.Add($"[System.State] IN ({In(settings.AutoStates)})");
        var who = string.IsNullOrWhiteSpace(settings.AutoAssignedTo) ? "@Me" : settings.AutoAssignedTo.Trim();
        where.Add(who.Equals("@Me", StringComparison.OrdinalIgnoreCase) ? "[System.AssignedTo] = @Me" : $"[System.AssignedTo] = {Quote(who)}");
        if (settings.AutoAreaPaths.Count > 0)
            where.Add("(" + string.Join(" OR ", settings.AutoAreaPaths.Select(a => $"[System.AreaPath] UNDER {Quote(a)}")) + ")");
        return $"SELECT [System.Id] FROM WorkItems WHERE {string.Join(" AND ", where)} ORDER BY [System.ChangedDate] DESC";
    }
}
