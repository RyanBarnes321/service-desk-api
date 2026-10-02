namespace ServiceDesk.Api.Tests.Persistence;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PostgreSqlIntegrationFactAttribute : FactAttribute
{
    public PostgreSqlIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("SERVICE_DESK_INTEGRATION_CONNECTION_STRING")))
        {
            Skip = "Set SERVICE_DESK_INTEGRATION_CONNECTION_STRING to run PostgreSQL integration tests.";
        }
    }
}
