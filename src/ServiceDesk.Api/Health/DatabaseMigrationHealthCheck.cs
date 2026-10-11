using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServiceDesk.Infrastructure.Persistence;

namespace ServiceDesk.Api.Health;

public sealed class DatabaseMigrationHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ServiceDeskDbContext>();
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("The database is unreachable.");
            }

            var pendingMigrations = await dbContext.Database
                .GetPendingMigrationsAsync(cancellationToken);
            if (pendingMigrations.Any())
            {
                return HealthCheckResult.Unhealthy("Database migrations are pending.");
            }

            return HealthCheckResult.Healthy("The database is reachable and current.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy(
                "The database readiness check failed.",
                exception);
        }
    }
}
