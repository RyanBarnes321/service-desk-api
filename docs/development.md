# Development guide

## Prerequisites

- .NET 10 SDK
- Docker with Docker Compose
- `dotnet-ef` 10.x for host-driven migration work

Never commit `.env`, JWT signing keys, passwords, or complete connection strings.

## Container workflow

Copy the safe template and replace both secret placeholders with local-only random values:

```powershell
Copy-Item .env.example .env
docker compose config
docker compose up --build
docker compose ps
```

The stack contains:

- `postgres`: PostgreSQL 18.6 with a named volume and no host-published database port.
- `migrate`: a one-shot invocation of `ServiceDesk.Api.dll migrate`; it waits for PostgreSQL readiness and runs EF migrations before the API starts.
- `api`: the non-root runtime image, published on `API_PORT` (8080 by default).

The API container is healthy only when `GET /health` confirms both database connectivity and zero pending migrations. Readiness returns `503` for an unreachable or out-of-date database. OpenAPI is served at `/openapi/v1.json`.

The migration service is intentionally separate from API startup. This avoids multiple API replicas racing to migrate. Run migrations as one controlled deployment step before rolling out application replicas.

Stop while retaining data:

```powershell
docker compose down
```

Delete the local database volume only when a full reset is intended:

```powershell
docker compose down -v
```

## Host workflow

For a PostgreSQL instance that is intentionally reachable from the host, provide the full connection string through configuration:

```powershell
$env:ConnectionStrings__ServiceDesk = "Host=localhost;Port=<port>;Database=service_desk;Username=servicedesk;Password=<local-password>"
dotnet ef database update --project src/ServiceDesk.Infrastructure --startup-project src/ServiceDesk.Api
```

The tracked `appsettings.json` contains no credentials. Generate migrations only for reviewed model changes:

```powershell
dotnet ef migrations add <MigrationName> --project src/ServiceDesk.Infrastructure --startup-project src/ServiceDesk.Api
dotnet ef migrations has-pending-model-changes --project src/ServiceDesk.Infrastructure --startup-project src/ServiceDesk.Api
```

The application also exposes the same controlled migration command used by Compose:

```powershell
dotnet run --project src/ServiceDesk.Api -- migrate
```

## Authentication configuration

Required configuration:

- `Jwt:SigningKey`: random secret of at least 32 bytes; HS256 only.
- `Jwt:Issuer`
- `Jwt:Audience`

For host development, keep the signing key in user secrets:

```powershell
dotnet user-secrets set "Jwt:SigningKey" "<random-local-secret-at-least-32-bytes>" --project src/ServiceDesk.Api
$env:Jwt__Issuer = "service-desk-api"
$env:Jwt__Audience = "service-desk-clients"
```

Startup fails when configuration is missing or the signing key is weak. Tokens last 60 minutes and validate signature, exact HS256 algorithm, issuer, audience, and lifetime with UTC timestamps and no clock skew.

## Bootstrap the initial Administrator

The CLI is the only bootstrap surface and never prints the password:

```powershell
$env:BootstrapAdmin__Email = "admin@example.test"
$env:BootstrapAdmin__Password = "<local-password>"
$env:BootstrapAdmin__FirstName = "Local"
$env:BootstrapAdmin__LastName = "Administrator"
dotnet run --project src/ServiceDesk.Api -- bootstrap-admin
```

For Compose, use `docker compose run --rm` with the same four environment variables and the trailing `bootstrap-admin` command. Bootstrap normalizes email, hashes the password, creates an active Administrator only when the users table is empty, and refuses otherwise. Its serializable transaction prevents a silent concurrent success, but there is no schema singleton constraint; a losing simultaneous command fails rather than being retried.

## Login and Bearer usage

Start the host API on the same port used by the examples:

```powershell
dotnet run --project src/ServiceDesk.Api --no-launch-profile --urls http://localhost:8080
```

Then, from another terminal:

```powershell
$login = Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/auth/login" `
  -ContentType "application/json" `
  -Body '{"email":"admin@example.test","password":"<local-password>"}'
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
Invoke-RestMethod -Headers $headers -Uri "http://localhost:8080/api/tickets"
```

Missing or cryptographically invalid tokens receive `401`. A valid token whose subject is missing or inactive receives `403`. Protected endpoints use the current persisted role rather than trusting a possibly stale role claim.

## Tests

Normal verification is database-free:

```powershell
dotnet restore ServiceDesk.slnx
dotnet build ServiceDesk.slnx --no-restore
$env:SERVICE_DESK_INTEGRATION_CONNECTION_STRING = $null
dotnet test ServiceDesk.slnx --no-build
```

Run the opt-in PostgreSQL suite against an already migrated isolated database:

```powershell
$env:SERVICE_DESK_INTEGRATION_CONNECTION_STRING = $env:ConnectionStrings__ServiceDesk
dotnet test tests/ServiceDesk.Api.Tests --filter FullyQualifiedName~PostgreSqlPersistenceTests
```

Integration tests isolate their rows with generated identifiers and cleanup. The administrator race test coordinates both SERIALIZABLE transactions after their active-admin count reads, then requires one success, one mapped `40001` conflict, and one active Administrator remaining.

CI uses PostgreSQL 18.6, applies migrations, produces TRX output, and fails if no integration tests execute or if any are skipped or fail.

## Concurrency notes

Ticket mutations increment a shadow `tickets.version` and use the original value in the update predicate. A stale update returns `409` and the implicit transaction rolls back its pending history entries. No retry is performed.

Administrator demotion/deactivation runs at SERIALIZABLE isolation, reloading the acting Administrator and counting active Administrators inside the transaction. Only PostgreSQL SQLSTATE `40001`—directly, through `DbUpdateException`, or through the known write-time `InvalidOperationException -> DbUpdateException -> PostgresException` chain—is translated to a conflict. Unrelated failures propagate.

Comments are immutable and do not update the ticket version. A close and comment can race after the comment visibility/state check; this is an explicitly documented MVP limitation rather than hidden optimistic-concurrency behavior.
