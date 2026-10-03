using System.Text;

namespace ServiceDesk.Api.Security;

public sealed record JwtSettings(string SigningKey, string Issuer, string Audience)
{
    public static JwtSettings FromConfiguration(IConfiguration configuration)
    {
        var signingKey = configuration["Jwt:SigningKey"];
        var issuer = configuration["Jwt:Issuer"];
        var audience = configuration["Jwt:Audience"];

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException("Jwt:SigningKey is required.");
        }

        if (Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
        }

        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new InvalidOperationException("Jwt:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException("Jwt:Audience is required.");
        }

        return new JwtSettings(signingKey, issuer, audience);
    }
}
