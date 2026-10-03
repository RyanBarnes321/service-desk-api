using Microsoft.EntityFrameworkCore;
using ServiceDesk.Core.Application.Authentication.Login;

namespace ServiceDesk.Infrastructure.Persistence;

public sealed class LoginUserQuery(ServiceDeskDbContext dbContext) : ILoginUserQuery
{
    public Task<LoginUser?> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        return dbContext.Users
            .AsNoTracking()
            .Where(user => user.Email == normalizedEmail)
            .Select(user => new LoginUser(
                user.Id,
                user.Email,
                user.PasswordHash,
                user.Role,
                user.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
