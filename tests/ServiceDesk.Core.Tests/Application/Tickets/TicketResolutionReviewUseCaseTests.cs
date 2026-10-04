using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Tickets.Assignment;
using ServiceDesk.Core.Application.Tickets.ResolutionReview;
using ServiceDesk.Core.Application.Tickets.TechnicalOperations;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Tests.Application.Tickets;

public class TicketResolutionReviewUseCaseTests
{
    private static readonly Guid AdministratorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TechnicianId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OccurredAt = CreatedAt.AddHours(2);

    [Fact]
    public async Task CloseAsync_ResolvedTicket_ClosesAndPersistsExactHistories()
    {
        var ticket = TicketInStatus(TicketStatus.Resolved);
        var summary = ticket.ResolutionSummary;
        var resolvedAt = ticket.ResolvedAt;
        var persistence = new RecordingPersistence(ticket);
        var time = new CountingTimeProvider(OccurredAt);

        var result = await UseCase(persistence, time).CloseAsync(ticket.Id, Administrator());

        Assert.Equal(TicketResolutionReviewOutcome.Success, result.Outcome);
        Assert.Same(ticket, persistence.PersistedTicket);
        Assert.Equal(1, persistence.FindCallCount);
        Assert.Equal(1, persistence.PersistCallCount);
        Assert.Equal(1, time.CallCount);
        Assert.Equal(TicketStatus.Closed, ticket.Status);
        Assert.Equal(TechnicianId, ticket.AssignedTechnicianId);
        Assert.Equal(summary, ticket.ResolutionSummary);
        Assert.Equal(resolvedAt, ticket.ResolvedAt);
        Assert.Equal(OccurredAt, ticket.ClosedAt);
        Assert.Equal(OccurredAt, ticket.UpdatedAt);
        Assert.Equal(ticket.ClosedAt, result.Ticket?.ClosedAt);
        AssertHistory(persistence.Histories[0], ticket.Id, TicketHistoryEventType.Closed, null, null);
        AssertHistory(persistence.Histories[1], ticket.Id, TicketHistoryEventType.StatusChanged,
            "Resolved", "Closed");
    }

    [Fact]
    public async Task RejectResolutionAsync_ResolvedTicket_ReopensAndPersistsRejectedSummary()
    {
        var ticket = TicketInStatus(TicketStatus.Resolved);
        var rejectedSummary = ticket.ResolutionSummary;
        var persistence = new RecordingPersistence(ticket);
        var time = new CountingTimeProvider(OccurredAt);

        var result = await UseCase(persistence, time)
            .RejectResolutionAsync(ticket.Id, Administrator());

        Assert.Equal(TicketResolutionReviewOutcome.Success, result.Outcome);
        Assert.Same(ticket, persistence.PersistedTicket);
        Assert.Equal(1, persistence.PersistCallCount);
        Assert.Equal(1, time.CallCount);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(TechnicianId, ticket.AssignedTechnicianId);
        Assert.Null(ticket.ResolutionSummary);
        Assert.Null(ticket.ResolvedAt);
        Assert.Null(ticket.ClosedAt);
        Assert.Equal(OccurredAt, ticket.UpdatedAt);
        AssertHistory(persistence.Histories[0], ticket.Id, TicketHistoryEventType.ResolutionRejected,
            rejectedSummary, null);
        AssertHistory(persistence.Histories[1], ticket.Id, TicketHistoryEventType.StatusChanged,
            "Resolved", "InProgress");
    }

    [Theory]
    [InlineData(UserRole.Employee)]
    [InlineData(UserRole.Technician)]
    public async Task ReviewOperation_NonAdministrator_ReturnsForbiddenBeforeLoad(UserRole role)
    {
        var persistence = new RecordingPersistence(TicketInStatus(TicketStatus.Resolved));
        var time = new CountingTimeProvider(OccurredAt);

        var close = await UseCase(persistence, time).CloseAsync(
            persistence.Ticket!.Id,
            new RequestActor(Guid.NewGuid(), role));
        var reject = await UseCase(persistence, time).RejectResolutionAsync(
            persistence.Ticket.Id,
            new RequestActor(Guid.NewGuid(), role));

        Assert.Equal(TicketResolutionReviewOutcome.Forbidden, close.Outcome);
        Assert.Equal(TicketResolutionReviewOutcome.Forbidden, reject.Outcome);
        Assert.Equal(0, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Equal(0, time.CallCount);
    }

    [Fact]
    public void ReviewOperation_UndefinedRole_FailsClosedAtActorConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RequestActor(Guid.NewGuid(), (UserRole)999));
    }

    [Theory]
    [InlineData("close")]
    [InlineData("reject")]
    public async Task ReviewOperation_MissingTicket_ReturnsNotFound(string operation)
    {
        var persistence = new RecordingPersistence(null);

        var result = await ExecuteAsync(UseCase(persistence), operation, Guid.NewGuid());

        Assert.Equal(TicketResolutionReviewOutcome.TicketNotFound, result.Outcome);
        Assert.Equal(1, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    public static TheoryData<string, TicketStatus> InvalidStates => new()
    {
        { "close", TicketStatus.Open },
        { "close", TicketStatus.Assigned },
        { "close", TicketStatus.InProgress },
        { "close", TicketStatus.Waiting },
        { "close", TicketStatus.Closed },
        { "reject", TicketStatus.Open },
        { "reject", TicketStatus.Assigned },
        { "reject", TicketStatus.InProgress },
        { "reject", TicketStatus.Waiting },
        { "reject", TicketStatus.Closed }
    };

    [Theory]
    [MemberData(nameof(InvalidStates))]
    public async Task ReviewOperation_InvalidState_ReturnsConflictWithoutPersistence(
        string operation,
        TicketStatus status)
    {
        var ticket = TicketInStatus(status);
        var persistence = new RecordingPersistence(ticket);

        var result = await ExecuteAsync(UseCase(persistence), operation, ticket.Id);

        Assert.Equal(TicketResolutionReviewOutcome.InvalidState, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Empty(persistence.Histories);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("reject")]
    public async Task ReviewOperation_ConcurrencyConflict_ReturnsDeliberateConflict(string operation)
    {
        var ticket = TicketInStatus(TicketStatus.Resolved);
        var persistence = new RecordingPersistence(
            ticket,
            TicketAssignmentPersistenceOutcome.ConcurrencyConflict);

        var result = await ExecuteAsync(UseCase(persistence), operation, ticket.Id);

        Assert.Equal(TicketResolutionReviewOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Equal(1, persistence.PersistCallCount);
    }

    [Fact]
    public async Task CloseAsync_UnrelatedPersistenceFailure_Propagates()
    {
        var expected = new InvalidOperationException("unexpected persistence failure");
        var ticket = TicketInStatus(TicketStatus.Resolved);
        var persistence = new RecordingPersistence(ticket, exception: expected);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UseCase(persistence).CloseAsync(ticket.Id, Administrator()));

        Assert.Same(expected, exception);
    }

    [Theory]
    [InlineData("close")]
    [InlineData("reject")]
    public async Task ReviewOperation_PreCancelled_DoesNotLoadReadTimeOrPersist(string operation)
    {
        var ticket = TicketInStatus(TicketStatus.Resolved);
        var persistence = new RecordingPersistence(ticket);
        var time = new CountingTimeProvider(OccurredAt);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation == "close"
            ? UseCase(persistence, time).CloseAsync(ticket.Id, Administrator(), source.Token)
            : UseCase(persistence, time).RejectResolutionAsync(ticket.Id, Administrator(), source.Token));

        Assert.Equal(0, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Equal(0, time.CallCount);
    }

    [Fact]
    public async Task RejectedTicket_CanBeResolvedAgainByAssignedTechnician_WithPriorAuditUnchanged()
    {
        var ticket = TicketInStatus(TicketStatus.InProgress);
        var persistence = new RecordingPersistence(ticket);
        const string firstSummary = "Replaced adapter.";
        var firstResolvedAt = OccurredAt.AddMinutes(-10);
        var technical = new TicketTechnicalOperationsUseCase(
            persistence,
            new CountingTimeProvider(firstResolvedAt));
        var technician = new RequestActor(TechnicianId, UserRole.Technician);

        var firstResolution = await technical.ResolveAsync(
            ticket.Id,
            firstSummary,
            technician);
        Assert.Equal(TicketTechnicalOperationOutcome.Success, firstResolution.Outcome);

        var review = UseCase(persistence, new CountingTimeProvider(OccurredAt));
        await review.RejectResolutionAsync(ticket.Id, Administrator());
        var secondSummary = "Reworked and verified the permanent fix.";
        var reResolvedAt = OccurredAt.AddMinutes(10);
        var secondTechnical = new TicketTechnicalOperationsUseCase(
            persistence,
            new CountingTimeProvider(reResolvedAt));

        var result = await secondTechnical.ResolveAsync(
            ticket.Id,
            secondSummary,
            technician);

        Assert.Equal(TicketTechnicalOperationOutcome.Success, result.Outcome);
        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Equal(secondSummary, ticket.ResolutionSummary);
        Assert.Equal(reResolvedAt, ticket.ResolvedAt);
        Assert.Equal(3, persistence.PersistCallCount);
        var resolutionEvents = persistence.AllHistories
            .Where(history => history.EventType is
                TicketHistoryEventType.Resolved or TicketHistoryEventType.ResolutionRejected)
            .ToArray();
        Assert.Equal(3, resolutionEvents.Length);
        Assert.Equal(TicketHistoryEventType.Resolved, resolutionEvents[0].EventType);
        Assert.Equal(firstSummary, resolutionEvents[0].NewValue);
        Assert.Equal(firstResolvedAt, resolutionEvents[0].CreatedAt);
        Assert.Equal(TicketHistoryEventType.ResolutionRejected, resolutionEvents[1].EventType);
        Assert.Equal(firstSummary, resolutionEvents[1].OldValue);
        Assert.Null(resolutionEvents[1].NewValue);
        Assert.Equal(OccurredAt, resolutionEvents[1].CreatedAt);
        Assert.Equal(TicketHistoryEventType.Resolved, resolutionEvents[2].EventType);
        Assert.Equal(secondSummary, resolutionEvents[2].NewValue);
        Assert.Equal(reResolvedAt, resolutionEvents[2].CreatedAt);
    }

    private static TicketResolutionReviewUseCase UseCase(
        RecordingPersistence persistence,
        TimeProvider? time = null) =>
        new(persistence, time ?? new CountingTimeProvider(OccurredAt));

    private static Task<TicketResolutionReviewResult> ExecuteAsync(
        TicketResolutionReviewUseCase useCase,
        string operation,
        Guid ticketId) => operation == "close"
        ? useCase.CloseAsync(ticketId, Administrator())
        : useCase.RejectResolutionAsync(ticketId, Administrator());

    private static RequestActor Administrator() => new(AdministratorId, UserRole.Administrator);

    private static Ticket TicketInStatus(TicketStatus status)
    {
        var ticket = Ticket.Create(
            "VPN unavailable",
            "The VPN connection fails consistently.",
            TicketCategory.Network,
            TicketPriority.High,
            Guid.NewGuid(),
            CreatedAt);
        if (status == TicketStatus.Open) return ticket;
        ticket.Assign(TechnicianId, CreatedAt.AddMinutes(1));
        if (status == TicketStatus.Assigned) return ticket;
        ticket.StartWork(CreatedAt.AddMinutes(2));
        if (status == TicketStatus.InProgress) return ticket;
        ticket.Wait(CreatedAt.AddMinutes(3));
        if (status == TicketStatus.Waiting) return ticket;
        ticket.Resume(CreatedAt.AddMinutes(4));
        ticket.Resolve("Resolved.", CreatedAt.AddMinutes(5));
        if (status == TicketStatus.Resolved) return ticket;
        ticket.Close(CreatedAt.AddMinutes(6));
        return ticket;
    }

    private static void AssertHistory(
        TicketHistory history,
        Guid ticketId,
        TicketHistoryEventType eventType,
        string? oldValue,
        string? newValue)
    {
        Assert.Equal(ticketId, history.TicketId);
        Assert.Equal(AdministratorId, history.PerformedByUserId);
        Assert.Equal(eventType, history.EventType);
        Assert.Equal(oldValue, history.OldValue);
        Assert.Equal(newValue, history.NewValue);
        Assert.Equal(OccurredAt, history.CreatedAt);
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
        public List<TicketHistory> AllHistories { get; } = [];

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
            AllHistories.AddRange(histories);
            if (exception is not null) throw exception;
            return Task.FromResult(outcome);
        }
    }

    private sealed class CountingTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public int CallCount { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            CallCount++;
            return timestamp;
        }
    }
}
