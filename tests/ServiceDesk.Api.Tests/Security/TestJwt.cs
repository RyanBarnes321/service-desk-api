using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Tests.Security;

internal static class TestJwt
{
    public const string SigningKey = "test-only-signing-key-that-is-at-least-sixty-four-characters-long-123456";
    public const string Issuer = "service-desk-tests";
    public const string Audience = "service-desk-test-client";

    public static string Create(
        Guid userId,
        DateTimeOffset? expiresAt = null,
        string issuer = Issuer,
        string audience = Audience,
        string signingKey = SigningKey,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var now = DateTimeOffset.UtcNow;
        var expiration = expiresAt ?? now.AddMinutes(10);
        var notBefore = expiration <= now ? expiration.AddMinutes(-10) : now.AddMinutes(-1);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, "user@example.com"),
                new Claim("role", UserRole.Employee.ToString())
            ],
            notBefore.UtcDateTime,
            expiration.UtcDateTime,
            new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                algorithm));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static Dictionary<string, string?> Configuration => new()
    {
        ["Jwt:SigningKey"] = SigningKey,
        ["Jwt:Issuer"] = Issuer,
        ["Jwt:Audience"] = Audience
    };
}
