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
```

The tracked `appsettings.json` contains only a credential-free connection structure. The environment variable supplies the local username and password.

## Configure authentication

The API requires `Jwt:SigningKey`, `Jwt:Issuer`, and `Jwt:Audience`. The tracked configuration contains only non-secret local issuer and audience identifiers. Store a random local signing key of at least 32 bytes with user secrets or an environment variable; never add it to tracked configuration:

```powershell
dotnet user-secrets set "Jwt:SigningKey" "<random-local-secret-at-least-32-bytes>" --project src/ServiceDesk.Api
```

Issuer and audience can be overridden when needed:

```powershell
$env:Jwt__Issuer = "service-desk-api"
$env:Jwt__Audience = "service-desk-clients"
```

Startup fails clearly when any required JWT value is absent or when the signing key is shorter than 32 bytes. Tokens are signature-, issuer-, audience-, and lifetime-validated with no clock-skew allowance.

## Bootstrap the first administrator

After applying migrations, provide the bootstrap values through local environment variables and run the CLI command. The password is never printed:

```powershell
$env:BootstrapAdmin__Email = "admin@example.test"
$env:BootstrapAdmin__Password = "<strong-local-password>"
$env:BootstrapAdmin__FirstName = "Local"
$env:BootstrapAdmin__LastName = "Administrator"
dotnet run --project src/ServiceDesk.Api -- bootstrap-admin
```

The command normalizes the email, hashes the password, creates an active Administrator only when the users table is empty, and exits without starting HTTP. It refuses when any user already exists. The database operation uses a serializable transaction; without an additional schema-level singleton constraint, simultaneous bootstrap commands can still race, in which case PostgreSQL aborts one transaction and the command fails rather than silently reporting success.

## Log in and call authenticated endpoints

Start the API in a separate terminal after bootstrapping the administrator:

```powershell
dotnet run --project src/ServiceDesk.Api
```

Then log in with the normalized-equivalent email and password:

```powershell
$login = Invoke-RestMethod -Method Post -Uri "https://localhost:<port>/api/auth/login" -ContentType "application/json" -Body '{"email":"admin@example.test","password":"<local-password>"}'
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
Invoke-RestMethod -Headers $headers -Uri "https://localhost:<port>/api/tickets"
```

Missing or cryptographically invalid tokens receive `401 Unauthorized`. A valid token whose subject no longer exists or is inactive receives `403 Forbidden`; every protected ticket request checks current database state instead of trusting token role or activity claims.

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
