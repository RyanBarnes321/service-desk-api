# Local development database

## Prerequisites

- .NET 10 SDK
- Docker with Docker Compose
- `dotnet-ef` 10.x

## Start PostgreSQL

Copy `.env.example` to `.env`, replace the password placeholder with a local-only password, then start the database:

```powershell
Copy-Item .env.example .env
docker compose config
docker compose up -d
docker compose ps
```

Compose starts PostgreSQL 18.6 as `service-desk-postgres`, creates the `service_desk` database and `servicedesk` user, and maps PostgreSQL to `localhost:5433` by default. Port 5433 avoids a common conflict with another local PostgreSQL instance; change `POSTGRES_PORT` in `.env` if needed. The ignored `.env` file is for local development only.

## Configure the API and apply migrations

Supply the complete connection string through standard .NET environment configuration. Replace the password placeholder with the same value used in `.env`:

```powershell
$env:ConnectionStrings__ServiceDesk = "Host=localhost;Port=5433;Database=service_desk;Username=servicedesk;Password=<your-local-password>"
dotnet ef database update --project src/ServiceDesk.Infrastructure --startup-project src/ServiceDesk.Api
dotnet run --project src/ServiceDesk.Api
```

The tracked `appsettings.json` contains only a credential-free connection structure. The environment variable supplies the local username and password.

## Run tests

The normal suite does not require PostgreSQL. Integration tests skip unless their dedicated connection-string variable is present:

```powershell
$env:SERVICE_DESK_INTEGRATION_CONNECTION_STRING = $null
dotnet test ServiceDesk.slnx
```

To run the PostgreSQL integration tests against the Compose database:

```powershell
$env:SERVICE_DESK_INTEGRATION_CONNECTION_STRING = $env:ConnectionStrings__ServiceDesk
dotnet test tests/ServiceDesk.Api.Tests --filter FullyQualifiedName~PostgreSqlPersistenceTests
```

## Stop or reset PostgreSQL

Stop the container while retaining its named volume:

```powershell
docker compose down
```

Remove the container and local database volume for a completely clean database:

```powershell
docker compose down -v
```
