using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Authentication.Login;

public sealed record LoginUser(
    Guid Id,
    string Email,
    string PasswordHash,
    UserRole Role,
    bool IsActive);
