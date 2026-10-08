using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.UserManagement;

public sealed record AdminUserResult(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    UserRole Role,
    bool IsActive,
    DateTimeOffset CreatedAt)
{
    public static AdminUserResult From(ServiceDesk.Core.Entities.User user) =>
        new(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Role,
            user.IsActive,
            user.CreatedAt);
}

public enum AdminUserOutcome
{
    Success,
    Forbidden,
    NotFound,
    DuplicateEmail,
    Conflict,
    SelfLockout,
    LastActiveAdministrator
}

public sealed record AdminUserExecution(
    AdminUserOutcome Outcome,
    AdminUserResult? User = null);

public sealed record ListAdminUsersRequest(int Page = 1, int PageSize = 20)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;
}

public sealed record ListAdminUsersResult(
    IReadOnlyList<AdminUserResult> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ListAdminUsersExecution(
    AdminUserOutcome Outcome,
    ListAdminUsersResult? Result = null);

public sealed record CreateAdminUserRequest(
    string? Email,
    string? Password,
    string? FirstName,
    string? LastName,
    UserRole Role);
