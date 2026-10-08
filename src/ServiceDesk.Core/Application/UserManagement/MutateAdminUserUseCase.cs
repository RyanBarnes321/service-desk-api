using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.UserManagement;

public sealed class MutateAdminUserUseCase(IAdminUserMutationPersistence persistence)
{
    public Task<AdminUserExecution> ChangeRoleAsync(
        Guid userId,
        UserRole role,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();
        if (actor.Role != UserRole.Administrator)
        {
            return Task.FromResult(new AdminUserExecution(AdminUserOutcome.Forbidden));
        }

        if (!Enum.IsDefined(role))
        {
            throw new AdminUserValidationException("Role must be a defined value.", "role");
        }

        return ExecuteAsync(userId, AdminUserMutation.ChangeRole, role, actor, cancellationToken);
    }

    public Task<AdminUserExecution> SetActivationAsync(
        Guid userId,
        bool? isActive,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();
        if (actor.Role != UserRole.Administrator)
        {
            return Task.FromResult(new AdminUserExecution(AdminUserOutcome.Forbidden));
        }

        if (isActive is null)
        {
            throw new AdminUserValidationException("IsActive is required.", "isActive");
        }

        return ExecuteAsync(
            userId,
            isActive.Value ? AdminUserMutation.Activate : AdminUserMutation.Deactivate,
            null,
            actor,
            cancellationToken);
    }

    public Task<AdminUserExecution> ActivateAsync(
        Guid userId,
        RequestActor actor,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(userId, AdminUserMutation.Activate, null, actor, cancellationToken);

    public Task<AdminUserExecution> DeactivateAsync(
        Guid userId,
        RequestActor actor,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(userId, AdminUserMutation.Deactivate, null, actor, cancellationToken);

    private Task<AdminUserExecution> ExecuteAsync(
        Guid userId,
        AdminUserMutation mutation,
        UserRole? role,
        RequestActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (userId == Guid.Empty)
        {
            throw new AdminUserValidationException("User ID cannot be empty.", nameof(userId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (actor.Role != UserRole.Administrator)
        {
            return Task.FromResult(new AdminUserExecution(AdminUserOutcome.Forbidden));
        }

        if (actor.UserId == userId &&
            (mutation == AdminUserMutation.Deactivate ||
             mutation == AdminUserMutation.ChangeRole && role != UserRole.Administrator))
        {
            return Task.FromResult(new AdminUserExecution(AdminUserOutcome.SelfLockout));
        }

        return persistence.ExecuteAsync(
            new AdminUserMutationRequest(actor.UserId, userId, mutation, role),
            cancellationToken);
    }
}
