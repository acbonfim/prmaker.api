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
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    public static bool IsValid(string? token, string signingKey)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            new JwtSecurityTokenHandler().ValidateToken(token, CreateValidationParameters(signingKey), out _);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string Create(string signingKey, string? subject, DateTime expiresAtUtc)
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrWhiteSpace(subject))
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, subject));

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
