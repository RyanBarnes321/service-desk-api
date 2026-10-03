using ServiceDesk.Core.Entities;

namespace ServiceDesk.Core.Application.Authentication.BootstrapAdmin;

public interface IBootstrapAdminPersistence
{
    Task<bool> CreateIfDatabaseEmptyAsync(User administrator, CancellationToken cancellationToken);
}
