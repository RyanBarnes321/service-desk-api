using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Authentication.Login;
using ServiceDesk.Core.Application.Tickets.GetTicket;
using ServiceDesk.Core.Application.Tickets.ListTickets;
using ServiceDesk.Core.Application.Tickets;
using ServiceDesk.Core.Application.Tickets.Assignment;
using ServiceDesk.Core.Security;
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
    public async Task AuthenticationQueries_NormalizedEmailAndActiveState_ReturnCurrentUserState()
    {
        var user = User.Create(
            EmailAddress.Normalize($"  AUTH-{Guid.NewGuid():N}@Example.COM "),
            "integration-password-hash",
            "Auth",
            "User",
            UserRole.Employee,
            UtcNowAtPostgreSqlPrecision());

        try
        {
            await using (var writeContext = CreateContext())
            {
                writeContext.Users.Add(user);
                await writeContext.SaveChangesAsync();
            }

            await using (var queryContext = CreateContext())
            {
                var loginQuery = new LoginUserQuery(queryContext);
                var stateQuery = new CurrentUserStateQuery(queryContext);
                var loginUser = await loginQuery.FindByNormalizedEmailAsync(
                    user.Email,
                    CancellationToken.None);

                Assert.NotNull(loginUser);
                Assert.Equal(user.Id, loginUser.Id);
                Assert.Equal(user.Email, loginUser.Email);
                var currentState = await stateQuery.FindAsync(user.Id, CancellationToken.None);
                Assert.NotNull(currentState);
                Assert.True(currentState.IsActive);
                Assert.Equal(user.Role, currentState.Role);
            }

            await using (var updateContext = CreateContext())
            {
                var storedUser = await updateContext.Users.SingleAsync(candidate => candidate.Id == user.Id);
                storedUser.Deactivate();
                await updateContext.SaveChangesAsync();
            }

            await using var inactiveContext = CreateContext();
            var inactiveStateQuery = new CurrentUserStateQuery(inactiveContext);
            var inactiveState = await inactiveStateQuery.FindAsync(user.Id, CancellationToken.None);
            Assert.NotNull(inactiveState);
            Assert.False(inactiveState.IsActive);
        }
        finally
        {
            await DeleteUserAsync(user.Id);
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task PersistAsync_ValidTicketAndCreatedHistory_PersistsBoth()
    {
        var createdAt = UtcNowAtPostgreSqlPrecision();
        var requester = User.Create(
            $"requester-{Guid.NewGuid():N}@example.com",
            "requester-password-hash",
            "Jordan",
            "Quinn",
            UserRole.Employee,
            createdAt);
        var ticket = Ticket.Create(
            "Cannot connect to VPN",
            "The VPN client times out during connection.",
            TicketCategory.Network,
            TicketPriority.High,
            requester.Id,
            createdAt);
        var history = TicketHistory.Create(
            ticket.Id,
            requester.Id,
            TicketHistoryEventType.Created,
            null,
            null,
            createdAt);

        try
        {
            await using (var setupContext = CreateContext())
            {
                setupContext.Users.Add(requester);
                await setupContext.SaveChangesAsync();
            }

            await using (var writeContext = CreateContext())
            {
                var persistence = new CreateTicketPersistence(writeContext);
                await persistence.PersistAsync(ticket, history, CancellationToken.None);
            }

            await using var readContext = CreateContext();
            var reloadedTicket = await readContext.Tickets
                .SingleAsync(candidate => candidate.Id == ticket.Id);
            var reloadedHistory = await readContext.TicketHistory
                .SingleAsync(candidate => candidate.Id == history.Id);

            Assert.Equal(ticket.Id, reloadedTicket.Id);
            Assert.Equal(ticket.CreatedAt, reloadedTicket.CreatedAt);
            Assert.Equal(ticket.Id, reloadedHistory.TicketId);
            Assert.Equal<Guid?>(requester.Id, reloadedHistory.PerformedByUserId);
            Assert.Equal(TicketHistoryEventType.Created, reloadedHistory.EventType);
            Assert.Null(reloadedHistory.OldValue);
            Assert.Null(reloadedHistory.NewValue);
            Assert.Equal(ticket.CreatedAt, reloadedHistory.CreatedAt);
        }
        finally
        {
            await DeleteTicketGraphAsync(ticket.Id, requester.Id, requester.Id);
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

    [PostgreSqlIntegrationFact]
    public async Task ListAsync_CombinedFiltersLiteralSearchSortingAndPagination_ReturnExpectedPage()
    {
        var createdAt = UtcNowAtPostgreSqlPrecision();
        var requester = User.Create(
            $"requester-{Guid.NewGuid():N}@example.com",
            "requester-password-hash",
            "Casey",
            "Morgan",
            UserRole.Employee,
            createdAt);
        var otherRequester = User.Create(
            $"requester-{Guid.NewGuid():N}@example.com",
            "requester-password-hash",
            "Jamie",
            "Rivera",
            UserRole.Employee,
            createdAt);
        var technician = User.Create(
            $"technician-{Guid.NewGuid():N}@example.com",
            "technician-password-hash",
            "Taylor",
            "Lee",
            UserRole.Technician,
            createdAt);
        var oldest = AssignedTicket(
            "Literal %_\\ Marker in title",
            "First matching ticket.",
            TicketCategory.Network,
            requester.Id,
            technician.Id,
            createdAt.AddMinutes(1));
        var newestA = AssignedTicket(
            "Second matching ticket",
            "LITERAL %_\\ MARKER in description.",
            TicketCategory.Network,
            requester.Id,
            technician.Id,
            createdAt.AddMinutes(2));
        var newestB = AssignedTicket(
            "Another literal %_\\ marker",
            "Third matching ticket.",
            TicketCategory.Network,
            requester.Id,
            technician.Id,
            createdAt.AddMinutes(2));
        var wildcardDecoy = AssignedTicket(
            "Literal abcX\\ Marker should not match",
            "Wildcard decoy.",
            TicketCategory.Network,
            requester.Id,
            technician.Id,
            createdAt.AddMinutes(2));
        var filterDecoy = AssignedTicket(
            "Literal %_\\ Marker wrong category",
            "Filter decoy.",
            TicketCategory.Software,
            otherRequester.Id,
            technician.Id,
            createdAt.AddMinutes(2));
        var tickets = new[] { oldest, newestA, newestB, wildcardDecoy, filterDecoy };

        try
        {
            await using (var setupContext = CreateContext())
            {
                setupContext.Users.AddRange(requester, otherRequester, technician);
                setupContext.Tickets.AddRange(tickets);
                await setupContext.SaveChangesAsync();
            }

            var baseRequest = new ListTicketsRequest(
                TicketStatus.Assigned,
                TicketPriority.High,
                TicketCategory.Network,
                technician.Id,
                requester.Id,
                oldest.CreatedAt,
                newestA.CreatedAt,
                @"literal %_\ marker",
                1,
                2,
                TicketSortField.CreatedAt,
                TicketSortDirection.Desc);
            var expectedNewest = new[] { newestA, newestB }
                .OrderByDescending(ticket => ticket.Id)
                .Select(ticket => ticket.Id)
                .ToArray();

            await using var queryContext = CreateContext();
            var query = new ListTicketsQuery(queryContext);
            var broadVisibility = TicketVisibilityScope.For(
                new RequestActor(Guid.NewGuid(), UserRole.Technician));
            var firstDescendingPage = await query.ListAsync(
                baseRequest,
                broadVisibility,
                CancellationToken.None);
            var secondDescendingPage = await query.ListAsync(
                baseRequest with { Page = 2 },
                broadVisibility,
                CancellationToken.None);
            var firstAscendingPage = await query.ListAsync(
                baseRequest with { SortDirection = TicketSortDirection.Asc },
                broadVisibility,
                CancellationToken.None);

            Assert.Equal(expectedNewest, firstDescendingPage.Items.Select(item => item.Id));
            Assert.Equal(3, firstDescendingPage.TotalCount);
            Assert.Equal(2, firstDescendingPage.TotalPages);
            Assert.Equal(oldest.Id, Assert.Single(secondDescendingPage.Items).Id);
            Assert.Equal(oldest.Id, firstAscendingPage.Items[0].Id);
            Assert.Equal(
                expectedNewest.OrderBy(id => id).First(),
                firstAscendingPage.Items[1].Id);
        }
        finally
        {
            await DeleteTicketsAndUsersAsync(
                tickets.Select(ticket => ticket.Id),
                [requester.Id, otherRequester.Id, technician.Id]);
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task TicketReadQueries_EmployeeVisibility_ExcludesOtherCreatorBeforePagingAndCount()
    {
        var createdAt = UtcNowAtPostgreSqlPrecision();
        var employee = User.Create(
            $"employee-{Guid.NewGuid():N}@example.com",
            "employee-password-hash",
            "Morgan",
            "Taylor",
            UserRole.Employee,
            createdAt);
        var otherEmployee = User.Create(
            $"employee-{Guid.NewGuid():N}@example.com",
            "employee-password-hash",
            "Riley",
            "Jordan",
            UserRole.Employee,
            createdAt);
        var ownTicket = Ticket.Create(
            "Employee-owned ticket",
            "This ticket is visible to its creator.",
            TicketCategory.Hardware,
            TicketPriority.Medium,
            employee.Id,
            createdAt.AddMinutes(1));
        var otherTicket = Ticket.Create(
            "Other employee ticket",
            "This ticket must not be visible to the first employee.",
            TicketCategory.Software,
            TicketPriority.High,
            otherEmployee.Id,
            createdAt.AddMinutes(2));
        var tickets = new[] { ownTicket, otherTicket };

        try
        {
            await using (var setupContext = CreateContext())
            {
                setupContext.Users.AddRange(employee, otherEmployee);
                setupContext.Tickets.AddRange(tickets);
                await setupContext.SaveChangesAsync();
            }

            await using var queryContext = CreateContext();
            var visibility = TicketVisibilityScope.For(
                new RequestActor(employee.Id, UserRole.Employee));
            var getQuery = new GetTicketQuery(queryContext);
            var listQuery = new ListTicketsQuery(queryContext);

            var ownResult = await getQuery.FindAsync(
                ownTicket.Id,
                visibility,
                CancellationToken.None);
            var inaccessibleResult = await getQuery.FindAsync(
                otherTicket.Id,
                visibility,
                CancellationToken.None);
            var page = await listQuery.ListAsync(
                new ListTicketsRequest(Page: 1, PageSize: 1),
                visibility,
                CancellationToken.None);

            Assert.NotNull(ownResult);
            Assert.Null(inaccessibleResult);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal(1, page.TotalPages);
            Assert.Equal(ownTicket.Id, Assert.Single(page.Items).Id);
        }
        finally
        {
            await DeleteTicketsAndUsersAsync(
                tickets.Select(ticket => ticket.Id),
                [employee.Id, otherEmployee.Id]);
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task AssignmentPersistence_ConcurrentClaims_RejectsStaleWriteAndRollsBackLosingHistory()
    {
        var createdAt = UtcNowAtPostgreSqlPrecision();
        var requester = User.Create(
            $"requester-{Guid.NewGuid():N}@example.com",
            "requester-password-hash",
            "Casey",
            "Morgan",
            UserRole.Employee,
            createdAt);
        var firstTechnician = User.Create(
            $"technician-{Guid.NewGuid():N}@example.com",
            "technician-password-hash",
            "Taylor",
            "Lee",
            UserRole.Technician,
            createdAt);
        var secondTechnician = User.Create(
            $"technician-{Guid.NewGuid():N}@example.com",
            "technician-password-hash",
            "Jordan",
            "Rivera",
            UserRole.Technician,
            createdAt);
        var ticket = Ticket.Create(
            "Concurrent claim",
            "Two technicians attempt to claim this ticket.",
            TicketCategory.Network,
            TicketPriority.High,
            requester.Id,
            createdAt);

        try
        {
            await using (var setupContext = CreateContext())
            {
                setupContext.Users.AddRange(requester, firstTechnician, secondTechnician);
                setupContext.Tickets.Add(ticket);
                await setupContext.SaveChangesAsync();
            }

            await using var firstContext = CreateContext();
            await using var staleContext = CreateContext();
            var firstPersistence = new TicketAssignmentPersistence(firstContext);
            var stalePersistence = new TicketAssignmentPersistence(staleContext);
            var firstTicket = await firstPersistence.FindTrackedAsync(ticket.Id, CancellationToken.None);
            var staleTicket = await stalePersistence.FindTrackedAsync(ticket.Id, CancellationToken.None);
            Assert.NotNull(firstTicket);
            Assert.NotNull(staleTicket);

            var firstTime = createdAt.AddMinutes(1);
            firstTicket.Assign(firstTechnician.Id, firstTime);
            var firstHistories = AssignmentHistories(
                firstTicket,
                firstTechnician.Id,
                firstTechnician.Id,
                firstTime);
            var staleTime = createdAt.AddMinutes(2);
            staleTicket.Assign(secondTechnician.Id, staleTime);
            var staleHistories = AssignmentHistories(
                staleTicket,
                secondTechnician.Id,
                secondTechnician.Id,
                staleTime);

            var firstOutcome = await firstPersistence.PersistAsync(
                firstTicket,
                firstHistories,
                CancellationToken.None);
            var staleOutcome = await stalePersistence.PersistAsync(
                staleTicket,
                staleHistories,
                CancellationToken.None);

            Assert.Equal(TicketAssignmentPersistenceOutcome.Persisted, firstOutcome);
            Assert.Equal(TicketAssignmentPersistenceOutcome.ConcurrencyConflict, staleOutcome);

            await using (var verificationContext = CreateContext())
            {
                var storedTicket = await verificationContext.Tickets
                    .SingleAsync(candidate => candidate.Id == ticket.Id);
                var storedHistories = await verificationContext.TicketHistory
                    .Where(history => history.TicketId == ticket.Id)
                    .OrderBy(history => history.EventType)
                    .ToArrayAsync();

                Assert.Equal(firstTechnician.Id, storedTicket.AssignedTechnicianId);
                Assert.Equal(TicketStatus.Assigned, storedTicket.Status);
                Assert.Equal(1L, verificationContext.Entry(storedTicket).Property<long>("Version").CurrentValue);
                Assert.Equal(2, storedHistories.Length);
                Assert.All(storedHistories, history =>
                    Assert.Equal<Guid?>(firstTechnician.Id, history.PerformedByUserId));
            }

            await using (var sequentialContext = CreateContext())
            {
                var sequentialPersistence = new TicketAssignmentPersistence(sequentialContext);
                var sequentialTicket = await sequentialPersistence.FindTrackedAsync(
                    ticket.Id,
                    CancellationToken.None);
                Assert.NotNull(sequentialTicket);
                var oldTechnicianId = sequentialTicket.AssignedTechnicianId;
                sequentialTicket.Reassign(secondTechnician.Id, createdAt.AddMinutes(3));
                var history = TicketHistory.Create(
                    ticket.Id,
                    firstTechnician.Id,
                    TicketHistoryEventType.Reassigned,
                    oldTechnicianId!.Value.ToString(),
                    secondTechnician.Id.ToString(),
                    createdAt.AddMinutes(3));

                var outcome = await sequentialPersistence.PersistAsync(
                    sequentialTicket,
                    [history],
                    CancellationToken.None);

                Assert.Equal(TicketAssignmentPersistenceOutcome.Persisted, outcome);
            }

            await using var finalContext = CreateContext();
            var finalTicket = await finalContext.Tickets.SingleAsync(candidate => candidate.Id == ticket.Id);
            Assert.Equal(2L, finalContext.Entry(finalTicket).Property<long>("Version").CurrentValue);
            Assert.Equal(secondTechnician.Id, finalTicket.AssignedTechnicianId);
        }
        finally
        {
            await DeleteTicketsAndUsersAsync(
                [ticket.Id],
                [requester.Id, firstTechnician.Id, secondTechnician.Id]);
        }
    }

    private static ServiceDeskDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ServiceDeskDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new ServiceDeskDbContext(options);
    }

    private static Ticket AssignedTicket(
        string title,
        string description,
        TicketCategory category,
        Guid requesterId,
        Guid technicianId,
        DateTimeOffset createdAt)
    {
        var ticket = Ticket.Create(
            title,
            description,
            category,
            TicketPriority.High,
            requesterId,
            createdAt);
        ticket.Assign(technicianId, createdAt);
        return ticket;
    }

    private static TicketHistory[] AssignmentHistories(
        Ticket ticket,
        Guid performedByUserId,
        Guid technicianId,
        DateTimeOffset occurredAt)
    {
        return
        [
            TicketHistory.Create(
                ticket.Id,
                performedByUserId,
                TicketHistoryEventType.Assigned,
                null,
                technicianId.ToString(),
                occurredAt),
            TicketHistory.Create(
                ticket.Id,
                performedByUserId,
                TicketHistoryEventType.StatusChanged,
                TicketStatus.Open.ToString(),
                TicketStatus.Assigned.ToString(),
                occurredAt)
        ];
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

    private static async Task DeleteTicketsAndUsersAsync(
        IEnumerable<Guid> ticketIds,
        IEnumerable<Guid> userIds)
    {
        await using var context = CreateContext();

        foreach (var ticketId in ticketIds)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM ticket_comments WHERE ticket_id = {ticketId}");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM ticket_history WHERE ticket_id = {ticketId}");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM tickets WHERE id = {ticketId}");
        }

        foreach (var userId in userIds)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE id = {userId}");
        }
    }
}
