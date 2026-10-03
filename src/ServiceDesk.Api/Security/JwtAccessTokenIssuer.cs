using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ServiceDesk.Core.Application.Authentication.Login;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Security;

public sealed class JwtAccessTokenIssuer(JwtSettings settings) : IAccessTokenIssuer
{
    public string Issue(
        Guid userId,
        string normalizedEmail,
        UserRole role,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            settings.Issuer,
            settings.Audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, normalizedEmail),
                new Claim("role", role.ToString())
            ],
            issuedAt.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
