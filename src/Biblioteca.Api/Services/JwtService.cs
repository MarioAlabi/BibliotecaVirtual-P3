using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Biblioteca.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace Biblioteca.Api.Services;

public class JwtService(IConfiguration config)
{
    public string GenerateToken(ApplicationUser user, IList<string> roles, int? lectorId = null)
    {
        var secret = config["Jwt:Key"] ?? config["JWT_SECRET"] ?? "super_secret_jwt_key_biblioteca_p3_2026_unicaes_secure!";
        var issuer = config["Jwt:Issuer"] ?? "BibliotecaApi";
        var audience = config["Jwt:Audience"] ?? "BibliotecaClient";
        var expireHours = config.GetValue<int?>("Jwt:ExpireHours") ?? 8;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.NombreCompleto ?? user.UserName ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        if (lectorId.HasValue)
        {
            claims.Add(new Claim("lectorId", lectorId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expireHours),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
