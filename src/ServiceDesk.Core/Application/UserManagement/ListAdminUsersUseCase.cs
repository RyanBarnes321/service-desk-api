using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.UserManagement;

public sealed class ListAdminUsersUseCase(IAdminUserQuery query)
{
    public async Task<ListAdminUsersExecution> ExecuteAsync(
        ListAdminUsersRequest request,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();

        if (actor.Role != UserRole.Administrator)
        {
            return new(AdminUserOutcome.Forbidden);
        }

        Validate(request);

        return new(
            AdminUserOutcome.Success,
            await query.ListAsync(request, cancellationToken));
    }

    private static void Validate(ListAdminUsersRequest request)
    {
        if (request.Page < 1)
        {
            throw new AdminUserValidationException("Page must be at least 1.", "page");
        }

        if (request.PageSize < 1 || request.PageSize > ListAdminUsersRequest.MaximumPageSize)
        {
            throw new AdminUserValidationException(
                $"Page size must be between 1 and {ListAdminUsersRequest.MaximumPageSize}.",
                "pageSize");
        }
    }
}
