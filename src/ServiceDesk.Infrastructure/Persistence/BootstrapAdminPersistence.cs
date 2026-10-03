using System.Data;
using Microsoft.EntityFrameworkCore;
using ServiceDesk.Core.Application.Authentication.BootstrapAdmin;
using ServiceDesk.Core.Entities;

namespace ServiceDesk.Infrastructure.Persistence;

public sealed class BootstrapAdminPersistence(ServiceDeskDbContext dbContext)
    : IBootstrapAdminPersistence
{
    public async Task<bool> CreateIfDatabaseEmptyAsync(
        User administrator,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            return false;
        }

        dbContext.Users.Add(administrator);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
