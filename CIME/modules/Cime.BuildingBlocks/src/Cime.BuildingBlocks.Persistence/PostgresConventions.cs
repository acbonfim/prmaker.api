using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cime.BuildingBlocks.Persistence;

/// <summary>
/// Convenções comuns dos contextos no PostgreSQL (feature 0015).
/// </summary>
public static class PostgresConventions
{
    /// <summary>
    /// <see cref="DateTime"/> vira <c>timestamp without time zone</c>, gravado e lido com
    /// <see cref="DateTimeKind.Unspecified"/> — exatamente como o MySQL/Pomelo fazia: o relógio é guardado
    /// como veio (UtcNow, ou a data do front via toISOString) e volta sem fuso, então o JSON continua
    /// sem "Z". Sem o conversor, o Npgsql recusa DateTime com Kind=Utc nessa coluna (e parâmetros de
    /// consulta vindos do JSON costumam ser Utc). <see cref="DateTimeOffset"/> segue o padrão do Npgsql
    /// (timestamptz, em UTC).
    /// </summary>
    public static ModelConfigurationBuilder UseUnspecifiedDateTimes(this ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTime>()
            .HaveColumnType("timestamp without time zone")
            .HaveConversion<UnspecifiedKindDateTimeConverter>();
        builder.Properties<DateTime?>()
            .HaveColumnType("timestamp without time zone")
            .HaveConversion<UnspecifiedKindDateTimeConverter>();
        return builder;
    }
}

/// <summary>Descarta o <see cref="DateTime.Kind"/> na ida e na volta (ver <see cref="PostgresConventions"/>).</summary>
public class UnspecifiedKindDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UnspecifiedKindDateTimeConverter()
        : base(v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified),
               v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified))
    {
    }
}
