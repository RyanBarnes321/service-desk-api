using Microsoft.EntityFrameworkCore;
using ServiceDesk.Core.Application.Tickets.CreateTicket;
using ServiceDesk.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CreateTicketUseCase>();
builder.Services.AddScoped<ICreateTicketPersistence, CreateTicketPersistence>();
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

app.Run();
