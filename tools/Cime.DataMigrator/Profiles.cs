using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Cime.DataMigrator;

/// <summary>
/// Um perfil = uma origem (SGBD antigo) + os contextos EF de destino no PostgreSQL. Tabelas, colunas,
/// chaves e a ordem das FKs são lidas dos modelos EF (nada de lista escrita à mão).
/// </summary>
public abstract class Profile
{
    public abstract string Name { get; }
    public abstract ISource CreateSource(string connectionString);
    protected abstract IEnumerable<(string Schema, Func<string, DbContext> Create)> Contexts { get; }

    /// <summary>Colunas usadas em buscas por igualdade: espaço no início/fim muda o resultado no Postgres.</summary>
    public abstract IReadOnlySet<string> LookupColumns { get; }

    public IEnumerable<string> Schemas => Contexts.Select(c => c.Schema);

    public IEnumerable<DbContext> CreateContexts(string targetConnection) => Contexts.Select(c => c.Create(targetConnection));

    /// <summary>Tabelas de todos os contextos, na ordem das FKs (pais antes dos filhos) dentro de cada contexto.</summary>
    public List<TableSpec> Tables(string targetConnection)
    {
        var all = new List<TableSpec>();
        foreach (var context in CreateContexts(targetConnection))
            using (context)
                all.AddRange(TopologicalOrder(context.Model));
        return all;
    }

    private static IEnumerable<TableSpec> TopologicalOrder(IModel model)
    {
        var tables = new Dictionary<string, (TableSpec Spec, HashSet<string> Parents)>();
        foreach (var entity in model.GetEntityTypes().Where(e => e.GetTableName() is not null && !e.IsOwned()))
        {
            var table = entity.GetTableName()!;
            var schema = entity.GetSchema() ?? throw new InvalidOperationException($"{table} sem schema");
            var store = StoreObjectIdentifier.Table(table, schema);
            var key = entity.FindPrimaryKey()!.Properties.Select(p => p.GetColumnName(store)!).ToArray();
            var parents = entity.GetForeignKeys()
                .Select(fk => fk.PrincipalEntityType.GetTableName()!)
                .Where(p => p != table).ToHashSet();
            tables[table] = (new TableSpec(schema, table, key), parents);
        }

        var done = new HashSet<string>();
        while (done.Count < tables.Count)
        {
            var ready = tables.Where(t => !done.Contains(t.Key) && t.Value.Parents.All(done.Contains)).Select(t => t.Key).OrderBy(n => n).ToList();
            if (ready.Count == 0) throw new InvalidOperationException("Ciclo de FKs entre tabelas: " + string.Join(", ", tables.Keys.Except(done)));
            foreach (var name in ready)
            {
                done.Add(name);
                yield return tables[name].Spec;
            }
        }
    }

    protected static DbContextOptions<T> Options<T>(string connection, string schema) where T : DbContext =>
        new DbContextOptionsBuilder<T>()
            .UseNpgsql(connection, o => o.MigrationsHistoryTable(HistoryTable, schema))
            .Options;

    public const string HistoryTable = "__EFMigrationsHistory";

    public static Profile Get(string? name) => name?.ToLowerInvariant() switch
    {
        "auth" => new AuthProfile(),
        "prform" => new PrformProfile(),
        _ => throw new InvalidOperationException("Perfil desconhecido. Use: auth | prform"),
    };
}

public record TableSpec(string Schema, string Name, string[] Key)
{
    public string Target => $"{Db.Q(Schema)}.{Db.Q(Name)}";
}

/// <summary>Cime.Auth: SQL Server (dbo) → schema auth (feature 0014).</summary>
public class AuthProfile : Profile
{
    public override string Name => "auth";
    public override ISource CreateSource(string cs) => new SqlServerSource(cs);
    protected override IEnumerable<(string, Func<string, DbContext>)> Contexts => new (string, Func<string, DbContext>)[]
    {
        (ProSales.Repository.Contexts.DefaultContext.Schema,
            cs => new ProSales.Repository.Contexts.DefaultContext(Options<ProSales.Repository.Contexts.DefaultContext>(cs, ProSales.Repository.Contexts.DefaultContext.Schema), null!)),
    };
    public override IReadOnlySet<string> LookupColumns { get; } = new HashSet<string>
        { "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "Name", "NormalizedName" };
}

/// <summary>API principal: MySQL (db31021) → schemas prform, vacations e timeline (feature 0015).</summary>
public class PrformProfile : Profile
{
    public override string Name => "prform";
    public override ISource CreateSource(string cs) => new MySqlSource(cs);
    protected override IEnumerable<(string, Func<string, DbContext>)> Contexts => new (string, Func<string, DbContext>)[]
    {
        (solvace.prform.Infra.Contexts.DefaultContext.Schema,
            cs => new solvace.prform.Infra.Contexts.DefaultContext(Options<solvace.prform.Infra.Contexts.DefaultContext>(cs, solvace.prform.Infra.Contexts.DefaultContext.Schema))),
        (solvace.vacations.infra.Contexts.VacationContext.Schema,
            cs => new solvace.vacations.infra.Contexts.VacationContext(Options<solvace.vacations.infra.Contexts.VacationContext>(cs, solvace.vacations.infra.Contexts.VacationContext.Schema))),
        (solvace.timeline.infra.Contexts.TimelineContext.Schema,
            cs => new solvace.timeline.infra.Contexts.TimelineContext(Options<solvace.timeline.infra.Contexts.TimelineContext>(cs, solvace.timeline.infra.Contexts.TimelineContext.Schema))),
    };
    public override IReadOnlySet<string> LookupColumns { get; } = new HashSet<string>
        { "CardNumber", "RepositoryId", "BranchPrefix", "BranchName", "EnvironmentName", "Name", "SourceMessageId" };
}
