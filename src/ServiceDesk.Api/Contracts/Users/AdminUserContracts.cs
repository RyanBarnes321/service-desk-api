using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Contracts.Users;

public sealed class ListAdminUsersQueryRequest
{
    public int? Page { get; init; }

    public int? PageSize { get; init; }
}

public sealed record CreateAdminUserHttpRequest(
    string? Email,
    string? Password,
    string? FirstName,
    string? LastName,
    UserRole? Role);

public sealed record ChangeAdminUserRoleRequest(UserRole? Role);

public sealed record SetAdminUserActivationRequest(bool? IsActive);

public sealed record AdminUserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    UserRole Role,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record ListAdminUsersResponse(
    IReadOnlyList<AdminUserResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
