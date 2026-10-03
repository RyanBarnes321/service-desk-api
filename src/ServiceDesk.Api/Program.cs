using Microsoft.EntityFrameworkCore;
using ServiceDesk.Api.Endpoints;
using ServiceDesk.Core.Application.Tickets.CreateTicket;
using ServiceDesk.Core.Application.Tickets.GetTicket;
using ServiceDesk.Infrastructure.Persistence;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CreateTicketUseCase>();
builder.Services.AddScoped<ICreateTicketPersistence, CreateTicketPersistence>();
builder.Services.AddScoped<GetTicketUseCase>();
builder.Services.AddScoped<IGetTicketQuery, GetTicketQuery>();
builder.Services.AddDbContext<ServiceDeskDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("ServiceDesk"),
        npgsqlOptions => npgsqlOptions.MigrationsHistoryTable("__ef_migrations_history")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapTicketEndpoints();

app.Run();

public partial class Program;
