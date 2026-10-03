using Microsoft.EntityFrameworkCore;
using ServiceDesk.Core.Application.Authentication;

namespace ServiceDesk.Infrastructure.Persistence;

public sealed class CurrentUserStateQuery(ServiceDeskDbContext dbContext) : ICurrentUserStateQuery
{
    public Task<CurrentUserState?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new CurrentUserState(user.Id, user.Role, user.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
