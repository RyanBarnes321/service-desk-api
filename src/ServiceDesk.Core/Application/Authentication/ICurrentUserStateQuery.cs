namespace ServiceDesk.Core.Application.Authentication;

public interface ICurrentUserStateQuery
{
    Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken);
}
