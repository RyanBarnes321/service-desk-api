using Microsoft.EntityFrameworkCore;
using ServiceDesk.Core.Entities;
using ServiceDesk.Infrastructure.Persistence;

namespace ServiceDesk.Api.Tests.Persistence;

public class ServiceDeskDbContextModelTests
{
    [Fact]
    public void Model_UsesExpectedTableNamesAndStringEnumConversions()
    {
        using var context = CreateContext();
        var model = context.Model;

        Assert.Equal("users", model.FindEntityType(typeof(User))!.GetTableName());
        Assert.Equal("tickets", model.FindEntityType(typeof(Ticket))!.GetTableName());
        Assert.Equal("ticket_comments", model.FindEntityType(typeof(TicketComment))!.GetTableName());
        Assert.Equal("ticket_history", model.FindEntityType(typeof(TicketHistory))!.GetTableName());

        Assert.Equal(
            typeof(string),
            model.FindEntityType(typeof(User))!
                .FindProperty(nameof(User.Role))!
                .GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(
            typeof(string),
            model.FindEntityType(typeof(Ticket))!
                .FindProperty(nameof(Ticket.Status))!
                .GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(
            typeof(string),
            model.FindEntityType(typeof(Ticket))!
                .FindProperty(nameof(Ticket.Priority))!
                .GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(
            typeof(string),
            model.FindEntityType(typeof(Ticket))!
                .FindProperty(nameof(Ticket.Category))!
                .GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(
            typeof(string),
            model.FindEntityType(typeof(TicketHistory))!
                .FindProperty(nameof(TicketHistory.EventType))!
                .GetTypeMapping().Converter!.ProviderClrType);
    }

    [Fact]
    public void Model_UsesRestrictiveDeleteBehaviorForEveryRelationship()
    {
        using var context = CreateContext();
        var foreignKeys = context.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .ToArray();

        Assert.Equal(6, foreignKeys.Length);
        Assert.All(foreignKeys, foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
    }

    [Fact]
    public void Model_ContainsOnlyTheAgreedIndexes()
    {
        using var context = CreateContext();
        var indexes = context.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetIndexes())
            .OrderBy(index => index.GetDatabaseName())
            .ToArray();

        Assert.Equal(
            [
                "ix_ticket_comments_ticket_id_created_at",
                "ix_ticket_history_ticket_id_created_at",
                "ix_tickets_assigned_technician_id",
                "ix_tickets_created_at",
                "ix_tickets_created_by_user_id",
                "ix_tickets_status",
                "ix_users_email"
            ],
            indexes.Select(index => index.GetDatabaseName()));

        var emailIndex = Assert.Single(indexes, index => index.GetDatabaseName() == "ix_users_email");
        Assert.True(emailIndex.IsUnique);
    }

    [Fact]
    public void Model_TicketVersion_IsBigintDefaultZeroConcurrencyToken()
    {
        using var context = CreateContext();
        var version = context.Model
            .FindEntityType(typeof(Ticket))!
            .FindProperty("Version")!;

        Assert.Equal(typeof(long), version.ClrType);
        Assert.Equal("version", version.GetColumnName());
        Assert.Equal("bigint", version.GetColumnType());
        Assert.Equal(0L, version.GetDefaultValue());
        Assert.True(version.IsConcurrencyToken);
    }

    private static ServiceDeskDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ServiceDeskDbContext>()
            .UseNpgsql("Host=localhost;Database=service_desk")
            .Options;

        return new ServiceDeskDbContext(options);
    }
}
