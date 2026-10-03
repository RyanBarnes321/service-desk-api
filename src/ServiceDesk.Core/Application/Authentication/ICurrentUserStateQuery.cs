namespace ServiceDesk.Core.Application.Authentication;

public interface ICurrentUserStateQuery
{
    Task<CurrentUserState?> FindAsync(Guid userId, CancellationToken cancellationToken);
}
