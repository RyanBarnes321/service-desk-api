using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;
using ServiceDesk.Core.Security;

namespace ServiceDesk.Core.Application.Authentication.BootstrapAdmin;

public sealed class BootstrapAdminUseCase(
    IBootstrapAdminPersistence persistence,
    IPasswordService passwordService,
    TimeProvider timeProvider)
{
    public async Task<BootstrapAdminOutcome> ExecuteAsync(
        BootstrapAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email, "email");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Password, "password");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FirstName, "firstName");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LastName, "lastName");

        var normalizedEmail = EmailAddress.Normalize(request.Email);
        var passwordHash = passwordService.Hash(request.Password);
        var administrator = User.Create(
            normalizedEmail,
            passwordHash,
            request.FirstName.Trim(),
            request.LastName.Trim(),
            UserRole.Administrator,
            timeProvider.GetUtcNow());
        var created = await persistence.CreateIfDatabaseEmptyAsync(administrator, cancellationToken);

        return created
            ? BootstrapAdminOutcome.Created
            : BootstrapAdminOutcome.RefusedExistingUsers;
    }
}
