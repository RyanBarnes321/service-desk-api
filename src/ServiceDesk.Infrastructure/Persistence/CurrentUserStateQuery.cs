using Microsoft.EntityFrameworkCore;
using ServiceDesk.Core.Application.Authentication;

namespace ServiceDesk.Infrastructure.Persistence;

public sealed class CurrentUserStateQuery(ServiceDeskDbContext dbContext) : ICurrentUserStateQuery
{
    public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId && user.IsActive, cancellationToken);
    }
}
