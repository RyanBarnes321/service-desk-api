using Microsoft.EntityFrameworkCore;
using ServiceDesk.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
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
