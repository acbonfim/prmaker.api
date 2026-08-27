using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using cliqx.auth.api.Models.Identity;

namespace cliqx.auth.api.Security;

public class TokenService
{
    public static async Task<string> GenerateJwtToken(User user, ICollection<string> roles)
    {

        var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim("ExternalId", user.ExternalId.ToString())
            };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.ASCII
            .GetBytes(Settings.Secret));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = creds
        };

        var tokenHandler = new JwtSecurityTokenHandler();

        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }

    public static string GenerateAccessToken(IEnumerable<Claim> claims, bool isXApiKey = false)
    {
        var key = new SymmetricSecurityKey(Encoding.ASCII
            .GetBytes(Settings.Secret));

        var tokenTypeClaim = new Claim("tokenType", isXApiKey ? "x-api-key" : "Bearer");
        claims = claims.Append(tokenTypeClaim);

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

        var tokeOptions = new JwtSecurityToken(
            issuer: "http://localhost:5000",
            audience: "http://localhost:5000",
            claims: claims,
            expires: !isXApiKey ? DateTime.Now.AddHours(1) : DateTime.Now.AddDays(-1),
            signingCredentials: creds
            
        );
        var tokenString = new JwtSecurityTokenHandler(){}.WriteToken(tokeOptions);
        return tokenString;
    }

    public static string GenerateRefreshToken(IEnumerable<Claim> claims, bool isXApiKey = false)
    {
        var key = new SymmetricSecurityKey(Encoding.ASCII
            .GetBytes(Settings.SecretRefresh));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

        var tokeOptions = new JwtSecurityToken(
            issuer: "http://localhost:5000",
            audience: "http://localhost:5000",
            claims: claims,
            expires: !isXApiKey ? DateTime.Now.AddDays(30) : null,
            signingCredentials: creds
        );
        var tokenString = new JwtSecurityTokenHandler().WriteToken(tokeOptions);
        return tokenString;
    }

    public static async Task<ClaimsPrincipal> GetPrincipalFromExpiredToken(string token)
    {
        try
        {
            var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false, //you might want to validate the audience and issuer depending on your use case
            ValidateIssuer = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Settings.SecretRefresh)),
            ValidateLifetime = true //here we are saying that we don't care about the token's expiration date
        };
        var tokenHandler = new JwtSecurityTokenHandler();
        SecurityToken securityToken;
        var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out securityToken);
        var jwtSecurityToken = securityToken as JwtSecurityToken;
        if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha512Signature, StringComparison.InvariantCultureIgnoreCase))
            throw new SecurityTokenException("Invalid token");
        
        return principal;
        
        }
        catch (System.Exception ex)
        {
            
             throw new SecurityTokenException("Invalid token");
        }
        
        
    }
}
