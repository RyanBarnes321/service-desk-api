using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.UserManagement;

public interface IAdminUserQuery
{
    Task<ListAdminUsersResult> ListAsync(
        ListAdminUsersRequest request,
        CancellationToken cancellationToken);

    Task<AdminUserResult?> FindAsync(Guid userId, CancellationToken cancellationToken);
}

public interface ICreateAdminUserPersistence
{
    Task<AdminUserOutcome> CreateAsync(User user, CancellationToken cancellationToken);
}

public enum AdminUserMutation
{
    ChangeRole,
    Activate,
    Deactivate
}

public sealed record AdminUserMutationRequest(
    Guid ActingAdministratorId,
    Guid TargetUserId,
    AdminUserMutation Mutation,
    UserRole? Role = null);

public interface IAdminUserMutationPersistence
{
    Task<AdminUserExecution> ExecuteAsync(
        AdminUserMutationRequest request,
        CancellationToken cancellationToken);
}
