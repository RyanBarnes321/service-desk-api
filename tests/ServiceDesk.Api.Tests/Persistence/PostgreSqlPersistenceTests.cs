using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;
using ServiceDesk.Infrastructure.Persistence;

namespace ServiceDesk.Api.Tests.Persistence;

public class PostgreSqlPersistenceTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("SERVICE_DESK_INTEGRATION_CONNECTION_STRING")!;

    [PostgreSqlIntegrationFact]
    public async Task User_RoundTripsThroughPostgreSql()
    {
        var createdAt = UtcNowAtPostgreSqlPrecision();
        var user = User.Create(
            $"technician-{Guid.NewGuid():N}@example.com",
            "integration-test-password-hash",
            "Taylor",
            "Morgan",
            UserRole.Technician,
            createdAt);

        try
        {
            await using (var writeContext = CreateContext())
            {
                writeContext.Users.Add(user);
                await writeContext.SaveChangesAsync();
            }

            await using var readContext = CreateContext();
            var reloaded = await readContext.Users.SingleAsync(candidate => candidate.Id == user.Id);

            Assert.Equal(user.Id, reloaded.Id);
            Assert.Equal(user.Email, reloaded.Email);
            Assert.Equal(user.PasswordHash, reloaded.PasswordHash);
            Assert.Equal(user.FirstName, reloaded.FirstName);
            Assert.Equal(user.LastName, reloaded.LastName);
            Assert.Equal(UserRole.Technician, reloaded.Role);
            Assert.Equal(createdAt, reloaded.CreatedAt);
            Assert.True(reloaded.IsActive);
        }
        finally
        {
            await DeleteUserAsync(user.Id);
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task TicketGraph_RoundTripsStringsRelationshipsAndRestrictiveForeignKey()
    {
        var createdAt = UtcNowAtPostgreSqlPrecision();
        var requester = User.Create(
            $"requester-{Guid.NewGuid():N}@example.com",
            "requester-password-hash",
            "Alex",
            "Rivera",
            UserRole.Employee,
            createdAt);
        var technician = User.Create(
            $"technician-{Guid.NewGuid():N}@example.com",
            "technician-password-hash",
            "Sam",
            "Lee",
            UserRole.Technician,
            createdAt);
        var ticket = Ticket.Create(
            "Cannot connect to office network",
            "The workstation cannot reach internal services.",
            TicketCategory.Network,
            TicketPriority.Medium,
            requester.Id,
            createdAt);

        ticket.Assign(technician.Id, createdAt.AddMinutes(1));
        ticket.StartWork(createdAt.AddMinutes(2));
        ticket.ChangePriority(TicketPriority.High, createdAt.AddMinutes(3));

        var comment = TicketComment.Create(
            ticket.Id,
            technician.Id,
            "Investigating the network adapter configuration.",
            createdAt.AddMinutes(4));
        var assignmentHistory = TicketHistory.Create(
            ticket.Id,
            technician.Id,
            TicketHistoryEventType.Assigned,
            null,
            technician.Id.ToString(),
            createdAt.AddMinutes(1));
        var priorityHistory = TicketHistory.Create(
            ticket.Id,
            technician.Id,
            TicketHistoryEventType.PriorityChanged,
            TicketPriority.Medium.ToString(),
            TicketPriority.High.ToString(),
            createdAt.AddMinutes(3));

        try
        {
            await using (var writeContext = CreateContext())
            {
                writeContext.Users.AddRange(requester, technician);
                writeContext.Tickets.Add(ticket);
                writeContext.TicketComments.Add(comment);
                writeContext.TicketHistory.AddRange(assignmentHistory, priorityHistory);
                await writeContext.SaveChangesAsync();
            }

            await using (var readContext = CreateContext())
            {
                var reloadedTicket = await readContext.Tickets
                    .SingleAsync(candidate => candidate.Id == ticket.Id);
                var reloadedComment = await readContext.TicketComments
                    .SingleAsync(candidate => candidate.Id == comment.Id);
                var reloadedHistory = await readContext.TicketHistory
                    .Where(history => history.TicketId == ticket.Id)
                    .OrderBy(history => history.CreatedAt)
                    .ToArrayAsync();

                Assert.Equal(TicketStatus.InProgress, reloadedTicket.Status);
                Assert.Equal(TicketPriority.High, reloadedTicket.Priority);
                Assert.Equal(TicketCategory.Network, reloadedTicket.Category);
                Assert.Equal(requester.Id, reloadedTicket.CreatedByUserId);
                Assert.Equal<Guid?>(technician.Id, reloadedTicket.AssignedTechnicianId);
                Assert.Equal(comment.Content, reloadedComment.Content);
                Assert.Equal(ticket.Id, reloadedComment.TicketId);
                Assert.Equal(technician.Id, reloadedComment.AuthorUserId);
                Assert.Equal(2, reloadedHistory.Length);
                Assert.Equal(TicketHistoryEventType.Assigned, reloadedHistory[0].EventType);
                Assert.Equal(TicketHistoryEventType.PriorityChanged, reloadedHistory[1].EventType);
            }

            var storedValues = await ReadStoredEnumValuesAsync(technician.Id, ticket.Id, priorityHistory.Id);
            Assert.Equal("Technician", storedValues.Role);
            Assert.Equal("InProgress", storedValues.Status);
            Assert.Equal("High", storedValues.Priority);
            Assert.Equal("Network", storedValues.Category);
            Assert.Equal("PriorityChanged", storedValues.EventType);

            await using var deleteContext = CreateContext();
            var referencedRequester = await deleteContext.Users.SingleAsync(user => user.Id == requester.Id);
            deleteContext.Users.Remove(referencedRequester);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => deleteContext.SaveChangesAsync());
            var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.RestrictViolation, postgresException.SqlState);
        }
        finally
        {
            await DeleteTicketGraphAsync(ticket.Id, requester.Id, technician.Id);
        }
    }

    private static ServiceDeskDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ServiceDeskDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ServiceDeskDbContext(options);
    }

    private static DateTimeOffset UtcNowAtPostgreSqlPrecision()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(
            now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond),
            TimeSpan.Zero);
    }

    private static async Task<(string Role, string Status, string Priority, string Category, string EventType)>
        ReadStoredEnumValuesAsync(Guid technicianId, Guid ticketId, Guid historyId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            SELECT u.role, t.status, t.priority, t.category, h.event_type
            FROM users AS u
            CROSS JOIN tickets AS t
            CROSS JOIN ticket_history AS h
            WHERE u.id = @technician_id
              AND t.id = @ticket_id
              AND h.id = @history_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("technician_id", technicianId);
        command.Parameters.AddWithValue("ticket_id", ticketId);
        command.Parameters.AddWithValue("history_id", historyId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        return (
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4));
    }

    private static async Task DeleteUserAsync(Guid userId)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE id = {userId}");
    }

    private static async Task DeleteTicketGraphAsync(Guid ticketId, Guid requesterId, Guid technicianId)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM ticket_comments WHERE ticket_id = {ticketId}");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM ticket_history WHERE ticket_id = {ticketId}");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tickets WHERE id = {ticketId}");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM users WHERE id IN ({requesterId}, {technicianId})");
    }
}
