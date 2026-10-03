using Microsoft.EntityFrameworkCore;
using ServiceDesk.Api.Endpoints;
using ServiceDesk.Api.Security;
using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Authentication.BootstrapAdmin;
using ServiceDesk.Core.Application.Authentication.Login;
using ServiceDesk.Core.Application.Tickets.CreateTicket;
using ServiceDesk.Core.Application.Tickets.GetTicket;
using ServiceDesk.Core.Application.Tickets.ListTickets;
using ServiceDesk.Core.Security;
using ServiceDesk.Infrastructure.Persistence;
using ServiceDesk.Infrastructure.Security;
using System.Text.Json.Serialization;

var isBootstrapAdmin = args.Length == 1 && args[0] == "bootstrap-admin";
var builder = WebApplication.CreateBuilder(isBootstrapAdmin ? [] : args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
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
builder.Services.AddScoped<GetTicketUseCase>();
builder.Services.AddScoped<IGetTicketQuery, GetTicketQuery>();
builder.Services.AddScoped<ListTicketsUseCase>();
builder.Services.AddScoped<IListTicketsQuery, ListTicketsQuery>();
builder.Services.AddDbContext<ServiceDeskDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("ServiceDesk"),
        npgsqlOptions => npgsqlOptions.MigrationsHistoryTable("__ef_migrations_history")));

if (!isBootstrapAdmin)
{
    builder.Services.AddServiceDeskAuthentication(builder.Configuration);
}

var app = builder.Build();

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthenticationEndpoints();
app.MapTicketEndpoints();

app.Run();

public partial class Program;
