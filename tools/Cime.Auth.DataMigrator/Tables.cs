namespace Cime.Auth.DataMigrator;

/// <summary>
/// Tabelas da auth, na ordem das foreign keys (pais antes dos filhos). Origem: dbo (SQL Server);
/// destino: schema auth (PostgreSQL). Os nomes de colunas são os mesmos dos dois lados (modelo EF).
/// </summary>
public static class Tables
{
    public const string TargetSchema = "auth";
    public const string HistoryTable = "__EFMigrationsHistory";

    public static readonly TableSpec[] All =
    {
        new("AspNetRoles", "Id"),
        new("AspNetUsers", "Id"),
        new("Services", "Id"),
        new("AspNetRoleClaims", "Id"),
        new("AspNetUserClaims", "Id"),
        new("AspNetUserLogins", "LoginProvider", "ProviderKey"),
        new("AspNetUserRoles", "UserId", "RoleId"),
        new("AspNetUserTokens", "UserId", "LoginProvider", "Name"),
        new("UserServices", "ServiceId", "UserId"),
        new("UserForgetCodes", "Id"),
    };

    /// <summary>Colunas usadas em buscas por igualdade: espaço no fim muda o resultado no Postgres.</summary>
    public static readonly HashSet<string> LookupColumns = new(StringComparer.Ordinal)
    {
        "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "Name", "NormalizedName",
    };
}

public record TableSpec(string Name, params string[] Key);
