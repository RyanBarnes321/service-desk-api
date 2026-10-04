using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Tickets.Assignment;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Tests.Application.Tickets;

public class TicketAssignmentUseCaseTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset OccurredAt = CreatedAt.AddHours(1);

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Administrator)]
    public async Task ClaimAsync_TechnicianOrAdministrator_AssignsSelfWithExactHistory(UserRole role)
    {
        var actor = Actor(role);
        var ticket = OpenTicket();
        var persistence = new RecordingPersistence(ticket);
        var time = new CountingTimeProvider(OccurredAt);
        var useCase = UseCase(persistence, timeProvider: time);

        var result = await useCase.ClaimAsync(ticket.Id, actor);

        Assert.Equal(TicketAssignmentOutcome.Success, result.Outcome);
        Assert.NotNull(result.Ticket);
        Assert.Equal(TicketStatus.Assigned, result.Ticket.Status);
        Assert.Equal(actor.UserId, result.Ticket.AssignedTechnicianId);
        Assert.Equal(OccurredAt, result.Ticket.UpdatedAt);
        Assert.Equal(1, time.CallCount);
        Assert.Equal(1, persistence.FindCallCount);
        Assert.Equal(1, persistence.PersistCallCount);
        Assert.Same(ticket, persistence.PersistedTicket);
        AssertHistories(
            persistence.Histories,
            actor.UserId,
            ticket.Id,
            (TicketHistoryEventType.Assigned, null, actor.UserId.ToString()),
            (TicketHistoryEventType.StatusChanged, "Open", "Assigned"));
    }

    [Fact]
    public async Task ClaimAsync_Employee_ReturnsForbiddenWithoutLoadingOrPersisting()
    {
        var persistence = new RecordingPersistence(OpenTicket());
        var time = new CountingTimeProvider(OccurredAt);
        var useCase = UseCase(persistence, timeProvider: time);

        var result = await useCase.ClaimAsync(
            Guid.NewGuid(),
            Actor(UserRole.Employee));

        Assert.Equal(TicketAssignmentOutcome.Forbidden, result.Outcome);
        Assert.Equal(0, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Equal(0, time.CallCount);
    }

    [Fact]
    public void AssignmentActor_UndefinedRole_FailsClosedBeforeUseCaseEntry()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RequestActor(Guid.NewGuid(), (UserRole)999));

        Assert.Equal("role", exception.ParamName);
    }

    [Fact]
    public async Task ClaimAsync_AlreadyAssigned_ReturnsInvalidStateWithoutPersisting()
    {
        var ticket = AssignedTicket(Guid.NewGuid());
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence);

        var result = await useCase.ClaimAsync(ticket.Id, Actor(UserRole.Technician));

        Assert.Equal(TicketAssignmentOutcome.InvalidState, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Empty(persistence.Histories);
    }

    [Fact]
    public async Task AssignAsync_OpenTicketAndActiveTechnician_AssignsWithExactHistory()
    {
        var actor = Actor(UserRole.Administrator);
        var targetId = Guid.NewGuid();
        var ticket = OpenTicket();
        var persistence = new RecordingPersistence(ticket);
        var users = ActiveTechnician(targetId);
        var useCase = UseCase(persistence, users);

        var result = await useCase.AssignAsync(ticket.Id, targetId, actor);

        Assert.Equal(TicketAssignmentOutcome.Success, result.Outcome);
        Assert.Equal(targetId, result.Ticket?.AssignedTechnicianId);
        Assert.Equal(1, users.CallCount);
        Assert.Equal(targetId, users.QueriedUserId);
        AssertHistories(
            persistence.Histories,
            actor.UserId,
            ticket.Id,
            (TicketHistoryEventType.Assigned, null, targetId.ToString()),
            (TicketHistoryEventType.StatusChanged, "Open", "Assigned"));
    }

    [Fact]
    public async Task AssignAsync_AssignedTicket_ReassignsWithoutRedundantStatusHistory()
    {
        var actor = Actor(UserRole.Administrator);
        var oldTechnicianId = Guid.NewGuid();
        var newTechnicianId = Guid.NewGuid();
        var ticket = AssignedTicket(oldTechnicianId);
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence, ActiveTechnician(newTechnicianId));

        var result = await useCase.AssignAsync(ticket.Id, newTechnicianId, actor);

        Assert.Equal(TicketAssignmentOutcome.Success, result.Outcome);
        Assert.Equal(newTechnicianId, result.Ticket?.AssignedTechnicianId);
        AssertHistories(
            persistence.Histories,
            actor.UserId,
            ticket.Id,
            (TicketHistoryEventType.Reassigned,
                oldTechnicianId.ToString(),
                newTechnicianId.ToString()));
    }

    [Theory]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Waiting)]
    public async Task AssignAsync_WorkingTicket_ReassignsAndAuditsStatusChange(TicketStatus status)
    {
        var actor = Actor(UserRole.Administrator);
        var oldTechnicianId = Guid.NewGuid();
        var newTechnicianId = Guid.NewGuid();
        var ticket = AssignedTicket(oldTechnicianId);
        ticket.StartWork(CreatedAt.AddMinutes(2));
        if (status == TicketStatus.Waiting)
        {
            ticket.Wait(CreatedAt.AddMinutes(3));
        }

        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence, ActiveTechnician(newTechnicianId));

        var result = await useCase.AssignAsync(ticket.Id, newTechnicianId, actor);

        Assert.Equal(TicketAssignmentOutcome.Success, result.Outcome);
        AssertHistories(
            persistence.Histories,
            actor.UserId,
            ticket.Id,
            (TicketHistoryEventType.Reassigned,
                oldTechnicianId.ToString(),
                newTechnicianId.ToString()),
            (TicketHistoryEventType.StatusChanged, status.ToString(), "Assigned"));
    }

    [Fact]
    public async Task UnassignAsync_AssignedTicket_UnassignsWithExactHistory()
    {
        var actor = Actor(UserRole.Administrator);
        var technicianId = Guid.NewGuid();
        var ticket = AssignedTicket(technicianId);
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence);

        var result = await useCase.UnassignAsync(ticket.Id, actor);

        Assert.Equal(TicketAssignmentOutcome.Success, result.Outcome);
        Assert.Equal(TicketStatus.Open, result.Ticket?.Status);
        Assert.Null(result.Ticket?.AssignedTechnicianId);
        AssertHistories(
            persistence.Histories,
            actor.UserId,
            ticket.Id,
            (TicketHistoryEventType.Unassigned, technicianId.ToString(), null),
            (TicketHistoryEventType.StatusChanged, "Assigned", "Open"));
    }

    [Theory]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Waiting)]
    public async Task UnassignAsync_WorkingTicket_UnassignsWithExactStatusHistory(TicketStatus status)
    {
        var actor = Actor(UserRole.Administrator);
        var technicianId = Guid.NewGuid();
        var ticket = TicketInStatus(technicianId, status);
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence);

        var result = await useCase.UnassignAsync(ticket.Id, actor);

        Assert.Equal(TicketAssignmentOutcome.Success, result.Outcome);
        Assert.Equal(TicketStatus.Open, result.Ticket?.Status);
        Assert.Null(result.Ticket?.AssignedTechnicianId);
        AssertHistories(
            persistence.Histories,
            actor.UserId,
            ticket.Id,
            (TicketHistoryEventType.Unassigned, technicianId.ToString(), null),
            (TicketHistoryEventType.StatusChanged, status.ToString(), "Open"));
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public async Task AssignAsync_ResolvedOrClosedTicket_ReturnsInvalidStateWithoutPersisting(
        TicketStatus status)
    {
        var newTechnicianId = Guid.NewGuid();
        var ticket = TicketInStatus(Guid.NewGuid(), status);
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence, ActiveTechnician(newTechnicianId));

        var result = await useCase.AssignAsync(
            ticket.Id,
            newTechnicianId,
            Actor(UserRole.Administrator));

        Assert.Equal(TicketAssignmentOutcome.InvalidState, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Empty(persistence.Histories);
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public async Task UnassignAsync_ResolvedOrClosedTicket_ReturnsInvalidStateWithoutPersisting(
        TicketStatus status)
    {
        var ticket = TicketInStatus(Guid.NewGuid(), status);
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence);

        var result = await useCase.UnassignAsync(
            ticket.Id,
            Actor(UserRole.Administrator));

        Assert.Equal(TicketAssignmentOutcome.InvalidState, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Empty(persistence.Histories);
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    public async Task AdminOperations_NonAdministrator_ReturnForbidden(UserRole role)
    {
        var targetId = Guid.NewGuid();
        var persistence = new RecordingPersistence(AssignedTicket(Guid.NewGuid()));
        var users = ActiveTechnician(targetId);
        var useCase = UseCase(persistence, users);

        var assign = await useCase.AssignAsync(Guid.NewGuid(), targetId, Actor(role));
        var unassign = await useCase.UnassignAsync(Guid.NewGuid(), Actor(role));

        Assert.Equal(TicketAssignmentOutcome.Forbidden, assign.Outcome);
        Assert.Equal(TicketAssignmentOutcome.Forbidden, unassign.Outcome);
        Assert.Equal(0, users.CallCount);
        Assert.Equal(0, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Theory]
    [InlineData(false, UserRole.Technician)]
    [InlineData(true, UserRole.Employee)]
    [InlineData(true, UserRole.Administrator)]
    public async Task AssignAsync_UnusableTarget_ReturnsGenericOutcomeWithoutTicketMutation(
        bool active,
        UserRole targetRole)
    {
        var targetId = Guid.NewGuid();
        var ticket = OpenTicket();
        var users = new RecordingUserStateQuery(
            new CurrentUserState(targetId, targetRole, active));
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence, users);

        var result = await useCase.AssignAsync(
            ticket.Id,
            targetId,
            Actor(UserRole.Administrator));

        Assert.Equal(TicketAssignmentOutcome.TargetUnavailable, result.Outcome);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Null(ticket.AssignedTechnicianId);
        Assert.Equal(0, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Fact]
    public async Task AssignAsync_MissingTargetAndEmptyTarget_ReturnSameOutcome()
    {
        var persistence = new RecordingPersistence(OpenTicket());
        var missingUsers = new RecordingUserStateQuery(null);
        var useCase = UseCase(persistence, missingUsers);
        var actor = Actor(UserRole.Administrator);

        var missing = await useCase.AssignAsync(Guid.NewGuid(), Guid.NewGuid(), actor);
        var empty = await useCase.AssignAsync(Guid.NewGuid(), Guid.Empty, actor);

        Assert.Equal(TicketAssignmentOutcome.TargetUnavailable, missing.Outcome);
        Assert.Equal(TicketAssignmentOutcome.TargetUnavailable, empty.Outcome);
        Assert.Equal(0, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Fact]
    public async Task ClaimAsync_MissingTicket_ReturnsNotFoundWithoutPersisting()
    {
        var persistence = new RecordingPersistence(null);
        var useCase = UseCase(persistence);

        var result = await useCase.ClaimAsync(
            Guid.NewGuid(),
            Actor(UserRole.Technician));

        Assert.Equal(TicketAssignmentOutcome.TicketNotFound, result.Outcome);
        Assert.Equal(1, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Fact]
    public async Task AssignAsync_SameTechnician_ReturnsInvalidStateWithoutPersisting()
    {
        var technicianId = Guid.NewGuid();
        var ticket = AssignedTicket(technicianId);
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence, ActiveTechnician(technicianId));

        var result = await useCase.AssignAsync(
            ticket.Id,
            technicianId,
            Actor(UserRole.Administrator));

        Assert.Equal(TicketAssignmentOutcome.InvalidState, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Empty(persistence.Histories);
    }

    [Fact]
    public async Task UnassignAsync_OpenTicket_ReturnsInvalidStateWithoutPersisting()
    {
        var ticket = OpenTicket();
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence);

        var result = await useCase.UnassignAsync(
            ticket.Id,
            Actor(UserRole.Administrator));

        Assert.Equal(TicketAssignmentOutcome.InvalidState, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Empty(persistence.Histories);
    }

    [Fact]
    public async Task ClaimAsync_ConcurrencyConflict_ReturnsConflictAfterOneAtomicPersistenceCall()
    {
        var persistence = new RecordingPersistence(
            OpenTicket(),
            TicketAssignmentPersistenceOutcome.ConcurrencyConflict);
        var useCase = UseCase(persistence);

        var result = await useCase.ClaimAsync(
            persistence.Ticket!.Id,
            Actor(UserRole.Technician));

        Assert.Equal(TicketAssignmentOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Equal(1, persistence.PersistCallCount);
        Assert.Equal(2, persistence.Histories.Count);
    }

    [Fact]
    public async Task ClaimAsync_UnexpectedPersistenceFailure_Propagates()
    {
        var persistence = new RecordingPersistence(
            OpenTicket(),
            exception: new InvalidOperationException("database failure"));
        var useCase = UseCase(persistence);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.ClaimAsync(persistence.Ticket!.Id, Actor(UserRole.Technician)));

        Assert.Equal("database failure", exception.Message);
    }

    [Fact]
    public async Task ClaimAsync_PreCancelled_DoesNotQueryMutateOrReadTime()
    {
        var ticket = OpenTicket();
        var persistence = new RecordingPersistence(ticket);
        var time = new CountingTimeProvider(OccurredAt);
        var useCase = UseCase(persistence, timeProvider: time);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ClaimAsync(ticket.Id, Actor(UserRole.Technician), source.Token));

        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(0, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Equal(0, time.CallCount);
    }

    private static TicketAssignmentUseCase UseCase(
        RecordingPersistence persistence,
        RecordingUserStateQuery? users = null,
        CountingTimeProvider? timeProvider = null)
    {
        return new TicketAssignmentUseCase(
            persistence,
            users ?? new RecordingUserStateQuery(null),
            timeProvider ?? new CountingTimeProvider(OccurredAt));
    }

    private static RecordingUserStateQuery ActiveTechnician(Guid id)
    {
        return new RecordingUserStateQuery(
            new CurrentUserState(id, UserRole.Technician, true));
    }

    private static RequestActor Actor(UserRole role) => new(Guid.NewGuid(), role);

    private static Ticket OpenTicket()
    {
        return Ticket.Create(
            "VPN unavailable",
            "The VPN connection fails consistently.",
            TicketCategory.Network,
            TicketPriority.High,
            Guid.NewGuid(),
            CreatedAt);
    }

    private static Ticket AssignedTicket(Guid technicianId)
    {
        var ticket = OpenTicket();
        ticket.Assign(technicianId, CreatedAt.AddMinutes(1));
        return ticket;
    }

    private static Ticket TicketInStatus(Guid technicianId, TicketStatus status)
    {
        var ticket = AssignedTicket(technicianId);
        if (status == TicketStatus.Assigned)
        {
            return ticket;
        }

        ticket.StartWork(CreatedAt.AddMinutes(2));
        if (status == TicketStatus.InProgress)
        {
            return ticket;
        }

        if (status == TicketStatus.Waiting)
        {
            ticket.Wait(CreatedAt.AddMinutes(3));
            return ticket;
        }

        ticket.Resolve("Issue resolved.", CreatedAt.AddMinutes(3));
        if (status == TicketStatus.Resolved)
        {
            return ticket;
        }

        ticket.Close(CreatedAt.AddMinutes(4));
        return ticket;
    }

    private static void AssertHistories(
        IReadOnlyList<TicketHistory> histories,
        Guid performedByUserId,
        Guid ticketId,
        params (TicketHistoryEventType Type, string? OldValue, string? NewValue)[] expected)
    {
        Assert.Equal(expected.Length, histories.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(ticketId, histories[index].TicketId);
            Assert.Equal<Guid?>(performedByUserId, histories[index].PerformedByUserId);
            Assert.Equal(expected[index].Type, histories[index].EventType);
            Assert.Equal(expected[index].OldValue, histories[index].OldValue);
            Assert.Equal(expected[index].NewValue, histories[index].NewValue);
            Assert.Equal(OccurredAt, histories[index].CreatedAt);
        }
    }

    private sealed class RecordingPersistence(
        Ticket? ticket,
        TicketAssignmentPersistenceOutcome outcome = TicketAssignmentPersistenceOutcome.Persisted,
        Exception? exception = null) : ITicketAssignmentPersistence
    {
        public Ticket? Ticket { get; } = ticket;

        public int FindCallCount { get; private set; }

        public int PersistCallCount { get; private set; }

        public Ticket? PersistedTicket { get; private set; }

        public IReadOnlyList<TicketHistory> Histories { get; private set; } = [];

        public Task<Ticket?> FindTrackedAsync(Guid ticketId, CancellationToken cancellationToken)
        {
            FindCallCount++;
            return Task.FromResult(Ticket?.Id == ticketId ? Ticket : null);
        }

        public Task<TicketAssignmentPersistenceOutcome> PersistAsync(
            Ticket persistedTicket,
            IReadOnlyCollection<TicketHistory> histories,
            CancellationToken cancellationToken)
        {
            PersistCallCount++;
            PersistedTicket = persistedTicket;
            Histories = histories.ToArray();
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(outcome);
        }
    }

    private sealed class RecordingUserStateQuery(CurrentUserState? state) : ICurrentUserStateQuery
    {
        public int CallCount { get; private set; }

        public Guid QueriedUserId { get; private set; }

        public Task<CurrentUserState?> FindAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            QueriedUserId = userId;
            return Task.FromResult(state);
        }
    }

    private sealed class CountingTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public int CallCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            CallCount++;
            return utcNow;
        }
    }
}
