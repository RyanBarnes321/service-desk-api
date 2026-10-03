using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Authentication.Login;

public interface IAccessTokenIssuer
{
    string Issue(
        Guid userId,
        string normalizedEmail,
        UserRole role,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt);
}
