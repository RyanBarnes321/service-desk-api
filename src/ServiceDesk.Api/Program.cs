using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServiceDesk.Api.Endpoints;
using ServiceDesk.Api.Health;
using ServiceDesk.Api.OpenApi;
using ServiceDesk.Api.Security;
using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Authentication.BootstrapAdmin;
using ServiceDesk.Core.Application.Authentication.Login;
using ServiceDesk.Core.Application.Tickets.CreateTicket;
using ServiceDesk.Core.Application.Tickets.Assignment;
using ServiceDesk.Core.Application.Tickets.Comments;
using ServiceDesk.Core.Application.Tickets.GetTicket;
using ServiceDesk.Core.Application.Tickets.History;
using ServiceDesk.Core.Application.Tickets.ListTickets;
using ServiceDesk.Core.Application.Tickets.ResolutionReview;
using ServiceDesk.Core.Application.Tickets.TechnicalOperations;
using ServiceDesk.Core.Security;
using ServiceDesk.Core.Application.UserManagement;
using ServiceDesk.Infrastructure.Persistence;
using ServiceDesk.Infrastructure.Security;
using System.Text.Json.Serialization;

var isBootstrapAdmin = args.Length == 1 && args[0] == "bootstrap-admin";
var isMigrate = args.Length == 1 && args[0] == "migrate";
var isCommand = isBootstrapAdmin || isMigrate;
var builder = WebApplication.CreateBuilder(isCommand ? [] : args);

builder.Services.AddServiceDeskOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseMigrationHealthCheck>("database");
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordService, AspNetCorePasswordService>();
builder.Services.AddScoped<LoginUseCase>();
builder.Services.AddScoped<ILoginUserQuery, LoginUserQuery>();
builder.Services.AddScoped<ICurrentUserStateQuery, CurrentUserStateQuery>();
builder.Services.AddScoped<BootstrapAdminUseCase>();
builder.Services.AddScoped<IBootstrapAdminPersistence, BootstrapAdminPersistence>();
builder.Services.AddScoped<CreateTicketUseCase>();
builder.Services.AddScoped<ICreateTicketPersistence, CreateTicketPersistence>();
builder.Services.AddScoped<TicketAssignmentUseCase>();
builder.Services.AddScoped<ITicketMutationPersistence, TicketMutationPersistence>();
builder.Services.AddScoped<CreateTicketCommentUseCase>();
builder.Services.AddScoped<ListTicketCommentsUseCase>();
builder.Services.AddScoped<TicketCommentsPersistence>();
builder.Services.AddScoped<ICreateTicketCommentPersistence>(provider =>
    provider.GetRequiredService<TicketCommentsPersistence>());
builder.Services.AddScoped<IListTicketCommentsQuery>(provider =>
    provider.GetRequiredService<TicketCommentsPersistence>());
builder.Services.AddScoped<TicketTechnicalOperationsUseCase>();
builder.Services.AddScoped<TicketResolutionReviewUseCase>();
builder.Services.AddScoped<GetTicketUseCase>();
builder.Services.AddScoped<IGetTicketQuery, GetTicketQuery>();
builder.Services.AddScoped<ListTicketHistoryUseCase>();
builder.Services.AddScoped<IListTicketHistoryQuery, ListTicketHistoryQuery>();
builder.Services.AddScoped<ListTicketsUseCase>();
builder.Services.AddScoped<IListTicketsQuery, ListTicketsQuery>();
builder.Services.AddScoped<ListAdminUsersUseCase>();
builder.Services.AddScoped<GetAdminUserUseCase>();
builder.Services.AddScoped<CreateAdminUserUseCase>();
builder.Services.AddScoped<MutateAdminUserUseCase>();
builder.Services.AddScoped<AdminUserPersistence>();
builder.Services.AddScoped<IAdminUserQuery>(provider =>
    provider.GetRequiredService<AdminUserPersistence>());
builder.Services.AddScoped<ICreateAdminUserPersistence>(provider =>
    provider.GetRequiredService<AdminUserPersistence>());
builder.Services.AddScoped<IAdminUserMutationPersistence>(provider =>
    provider.GetRequiredService<AdminUserPersistence>());
builder.Services.AddDbContext<ServiceDeskDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("ServiceDesk"),
        npgsqlOptions => npgsqlOptions.MigrationsHistoryTable("__ef_migrations_history")));

if (!isCommand)
{
    builder.Services.AddServiceDeskAuthentication(builder.Configuration);
}

var app = builder.Build();

if (isMigrate)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ServiceDeskDbContext>();
    await dbContext.Database.MigrateAsync();
    Console.WriteLine("Database migrations applied.");
    return;
}

if (isBootstrapAdmin)
{
    await using var scope = app.Services.CreateAsyncScope();
    var useCase = scope.ServiceProvider.GetRequiredService<BootstrapAdminUseCase>();

    try
    {
        var result = await useCase.ExecuteAsync(new BootstrapAdminRequest(
            builder.Configuration["BootstrapAdmin:Email"] ?? string.Empty,
            builder.Configuration["BootstrapAdmin:Password"] ?? string.Empty,
            builder.Configuration["BootstrapAdmin:FirstName"] ?? string.Empty,
            builder.Configuration["BootstrapAdmin:LastName"] ?? string.Empty));

        if (result == BootstrapAdminOutcome.Created)
        {
            Console.WriteLine("Administrator created.");
        }
        else
        {
            Console.Error.WriteLine("Bootstrap refused because the database already contains a user.");
            Environment.ExitCode = 1;
        }
    }
    catch (ArgumentException exception)
    {
        Console.Error.WriteLine($"Bootstrap configuration is invalid: {exception.ParamName} is required.");
        Environment.ExitCode = 1;
    }

    return;
}

app.MapOpenApi();
app.MapGet("/health", async (
        HealthCheckService healthChecks,
        CancellationToken cancellationToken) =>
    {
        var report = await healthChecks.CheckHealthAsync(cancellationToken);
        var response = new HealthResponse(report.Status.ToString());
        return report.Status == HealthStatus.Healthy
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
    })
    .AllowAnonymous()
    .WithTags("Operations")
    .WithSummary("Check readiness")
    .WithDescription(
        "Returns 200 only when PostgreSQL is reachable and every EF migration is applied; otherwise 503.")
    .Produces<HealthResponse>(StatusCodes.Status200OK)
    .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthenticationEndpoints();
app.MapTicketEndpoints();
app.MapAdminUserEndpoints();

app.Run();

public partial class Program;

public sealed record HealthResponse(string Status);
