using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Cime.BuildingBlocks.RealTime;

/// <summary>
/// Parâmetros dos tokens de conexão ao hub (JWT HS256). Compartilhado entre quem emite (API)
/// e quem valida (relay ou o hub em processo), para os dois lados nunca divergirem.
/// </summary>
public static class RealTimeTokens
{
    public const string Issuer = "cime-api";
    public const string Audience = "cime-realtime";
    /// <summary>Claims do token além do <c>sub</c> (0079): nome e papéis, para a presença e o grupo dos admins.</summary>
    public const string NameClaim = "name";
    public const string RoleClaim = "role";
    /// <summary>IP do navegador visto pela API ao emitir o token (o relay pode estar atrás de proxy).</summary>
    public const string IpClaim = "ip";

    public static SymmetricSecurityKey CreateKey(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));

    public static TokenValidationParameters CreateValidationParameters(string signingKey) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = CreateKey(signingKey),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        NameClaimType = NameClaim,
        RoleClaimType = RoleClaim
    };

    public static bool IsValid(string? token, string signingKey) => Validate(token, signingKey) is not null;

    /// <summary>Valida o token e devolve o principal com as claims como vieram (sub, name, role), ou null.</summary>
    public static ClaimsPrincipal? Validate(string? token, string signingKey)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            return new JwtSecurityTokenHandler { MapInboundClaims = false }
                .ValidateToken(token, CreateValidationParameters(signingKey), out _);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string Create(string signingKey, string? subject, DateTime expiresAtUtc, string? name = null, IEnumerable<string>? roles = null,
        string? ip = null)
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrWhiteSpace(subject))
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, subject));
        if (!string.IsNullOrWhiteSpace(name))
            claims.Add(new Claim(NameClaim, name));
        if (!string.IsNullOrWhiteSpace(ip))
            claims.Add(new Claim(IpClaim, ip));
        foreach (var role in (roles ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct())
            claims.Add(new Claim(RoleClaim, role));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: expiresAtUtc,
            signingCredentials: new SigningCredentials(CreateKey(signingKey), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
