using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.UserManagement;

public sealed class GetAdminUserUseCase(IAdminUserQuery query)
{
    public async Task<AdminUserExecution> ExecuteAsync(
        Guid userId,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();
        if (actor.Role != UserRole.Administrator)
        {
            return new(AdminUserOutcome.Forbidden);
        }

        if (userId == Guid.Empty)
        {
            throw new AdminUserValidationException("User ID cannot be empty.", nameof(userId));
        }

        var user = await query.FindAsync(userId, cancellationToken);
        return user is null
            ? new(AdminUserOutcome.NotFound)
            : new(AdminUserOutcome.Success, user);
    }
}
