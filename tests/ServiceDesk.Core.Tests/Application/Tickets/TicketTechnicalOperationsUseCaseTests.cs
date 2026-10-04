using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Tickets.Assignment;
using ServiceDesk.Core.Application.Tickets.TechnicalOperations;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Tests.Application.Tickets;

public class TicketTechnicalOperationsUseCaseTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset OccurredAt = CreatedAt.AddHours(1);

    [Fact]
    public async Task StartWorkAsync_AssignedTechnician_TransitionsAndPersistsExactHistory()
    {
        var actor = Technician();
        var ticket = TicketInStatus(actor.UserId, TicketStatus.Assigned);
        var persistence = new RecordingPersistence(ticket);
        var time = new CountingTimeProvider(OccurredAt);
        var useCase = UseCase(persistence, time);

        var result = await useCase.StartWorkAsync(ticket.Id, actor);

        AssertSuccess(result, ticket, TicketStatus.InProgress);
        Assert.Equal(1, time.CallCount);
        Assert.Equal(1, persistence.PersistCallCount);
        Assert.Same(ticket, persistence.PersistedTicket);
        AssertHistory(
            Assert.Single(persistence.Histories),
            ticket.Id,
            actor.UserId,
            TicketHistoryEventType.StatusChanged,
            "Assigned",
            "InProgress");
    }

    [Theory]
    [InlineData("start", UserRole.Employee)]
    [InlineData("start", UserRole.Administrator)]
    [InlineData("wait", UserRole.Employee)]
    [InlineData("wait", UserRole.Administrator)]
    [InlineData("resume", UserRole.Employee)]
    [InlineData("resume", UserRole.Administrator)]
    [InlineData("resolve", UserRole.Employee)]
    [InlineData("resolve", UserRole.Administrator)]
    [InlineData("priority", UserRole.Employee)]
    [InlineData("priority", UserRole.Administrator)]
    public async Task TechnicalOperation_NonTechnicianRole_ReturnsForbiddenBeforeLoad(
        string operation,
        UserRole role)
    {
        var persistence = new RecordingPersistence(null);
        var useCase = UseCase(persistence);

        var result = await ExecuteAsync(
            useCase,
            operation,
            Guid.NewGuid(),
            new RequestActor(Guid.NewGuid(), role));

        Assert.Equal(TicketTechnicalOperationOutcome.Forbidden, result.Outcome);
        Assert.Equal(0, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Fact]
    public void TechnicalOperation_UndefinedRole_FailsClosedAtActorConstruction()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RequestActor(Guid.NewGuid(), (UserRole)999));

        Assert.Equal("role", exception.ParamName);
    }

    [Fact]
    public async Task StartWorkAsync_DifferentTechnician_ReturnsForbiddenWithoutMutation()
    {
        var actor = Technician();
        var ticket = TicketInStatus(Guid.NewGuid(), TicketStatus.Assigned);
        var persistence = new RecordingPersistence(ticket);
        var time = new CountingTimeProvider(OccurredAt);
        var useCase = UseCase(persistence, time);

        var result = await useCase.StartWorkAsync(ticket.Id, actor);

        Assert.Equal(TicketTechnicalOperationOutcome.Forbidden, result.Outcome);
        Assert.Equal(TicketStatus.Assigned, ticket.Status);
        Assert.Equal(0, time.CallCount);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Empty(persistence.Histories);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("wait")]
    [InlineData("resume")]
    [InlineData("resolve")]
    [InlineData("priority")]
    public async Task TechnicalOperation_DifferentTechnician_ReturnsForbiddenWithoutMutation(
        string operation)
    {
        var actor = Technician();
        var ticket = TicketInStatus(Guid.NewGuid(), TicketStatus.Assigned);
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence);

        var result = await ExecuteAsync(useCase, operation, ticket.Id, actor);

        Assert.Equal(TicketTechnicalOperationOutcome.Forbidden, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Empty(persistence.Histories);
    }

    [Fact]
    public async Task StartWorkAsync_UnassignedTicket_ReturnsForbiddenWithoutMutation()
    {
        var ticket = OpenTicket();
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence);

        var result = await useCase.StartWorkAsync(ticket.Id, Technician());

        Assert.Equal(TicketTechnicalOperationOutcome.Forbidden, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Fact]
    public async Task StartWorkAsync_MissingTicket_ReturnsNotFound()
    {
        var persistence = new RecordingPersistence(null);
        var useCase = UseCase(persistence);

        var result = await useCase.StartWorkAsync(Guid.NewGuid(), Technician());

        Assert.Equal(TicketTechnicalOperationOutcome.TicketNotFound, result.Outcome);
        Assert.Equal(1, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("wait")]
    [InlineData("resume")]
    [InlineData("resolve")]
    [InlineData("priority")]
    public async Task TechnicalOperation_MissingTicket_ReturnsNotFound(string operation)
    {
        var persistence = new RecordingPersistence(null);
        var useCase = UseCase(persistence);

        var result = await ExecuteAsync(useCase, operation, Guid.NewGuid(), Technician());

        Assert.Equal(TicketTechnicalOperationOutcome.TicketNotFound, result.Outcome);
        Assert.Equal(1, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Fact]
    public async Task StartWorkAsync_InvalidState_ReturnsConflictWithoutHistoryOrPersistence()
    {
        var actor = Technician();
        var ticket = TicketInStatus(actor.UserId, TicketStatus.InProgress);
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence);

        var result = await useCase.StartWorkAsync(ticket.Id, actor);

        Assert.Equal(TicketTechnicalOperationOutcome.InvalidState, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Empty(persistence.Histories);
    }

    [Fact]
    public async Task WaitAsync_InProgress_TransitionsWithExactHistory()
    {
        var actor = Technician();
        var ticket = TicketInStatus(actor.UserId, TicketStatus.InProgress);
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence);

        var result = await useCase.WaitAsync(ticket.Id, actor);

        AssertSuccess(result, ticket, TicketStatus.Waiting);
        AssertHistory(
            Assert.Single(persistence.Histories),
            ticket.Id,
            actor.UserId,
            TicketHistoryEventType.StatusChanged,
            "InProgress",
            "Waiting");
    }

    [Theory]
    [InlineData(TicketStatus.Assigned)]
    [InlineData(TicketStatus.Waiting)]
    public async Task WaitAsync_InvalidState_ReturnsConflict(TicketStatus status)
    {
        var actor = Technician();
        var persistence = new RecordingPersistence(TicketInStatus(actor.UserId, status));
        var useCase = UseCase(persistence);

        var result = await useCase.WaitAsync(persistence.Ticket!.Id, actor);

        Assert.Equal(TicketTechnicalOperationOutcome.InvalidState, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Fact]
    public async Task ResumeAsync_Waiting_TransitionsWithExactHistory()
    {
        var actor = Technician();
        var ticket = TicketInStatus(actor.UserId, TicketStatus.Waiting);
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence);

        var result = await useCase.ResumeAsync(ticket.Id, actor);

        AssertSuccess(result, ticket, TicketStatus.InProgress);
        AssertHistory(
            Assert.Single(persistence.Histories),
            ticket.Id,
            actor.UserId,
            TicketHistoryEventType.StatusChanged,
            "Waiting",
            "InProgress");
    }

    [Fact]
    public async Task ResumeAsync_InvalidState_ReturnsConflict()
    {
        var actor = Technician();
        var persistence = new RecordingPersistence(
            TicketInStatus(actor.UserId, TicketStatus.InProgress));
        var useCase = UseCase(persistence);

        var result = await useCase.ResumeAsync(persistence.Ticket!.Id, actor);

        Assert.Equal(TicketTechnicalOperationOutcome.InvalidState, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Fact]
    public async Task ResolveAsync_InProgress_SetsResolutionAndPersistsBothExactHistories()
    {
        const string summary = " Replaced the failed adapter and verified connectivity. ";
        var actor = Technician();
        var ticket = TicketInStatus(actor.UserId, TicketStatus.InProgress);
        var persistence = new RecordingPersistence(ticket);
        var time = new CountingTimeProvider(OccurredAt);
        var useCase = UseCase(persistence, time);

        var result = await useCase.ResolveAsync(ticket.Id, summary, actor);

        AssertSuccess(result, ticket, TicketStatus.Resolved);
        Assert.Equal(summary, result.Ticket?.ResolutionSummary);
        Assert.Equal<DateTimeOffset?>(OccurredAt, result.Ticket?.ResolvedAt);
        Assert.Equal(1, time.CallCount);
        Assert.Equal(2, persistence.Histories.Count);
        AssertHistory(
            persistence.Histories[0],
            ticket.Id,
            actor.UserId,
            TicketHistoryEventType.Resolved,
            null,
            summary);
        AssertHistory(
            persistence.Histories[1],
            ticket.Id,
            actor.UserId,
            TicketHistoryEventType.StatusChanged,
            "InProgress",
            "Resolved");
    }

    [Fact]
    public async Task ResolveAsync_Waiting_ReturnsInvalidStateWithoutHistoryOrPersistence()
    {
        var actor = Technician();
        var ticket = TicketInStatus(actor.UserId, TicketStatus.Waiting);
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence);

        var result = await useCase.ResolveAsync(ticket.Id, "Resolved.", actor);

        Assert.Equal(TicketTechnicalOperationOutcome.InvalidState, result.Outcome);
        Assert.Equal(TicketStatus.Waiting, ticket.Status);
        Assert.Null(ticket.ResolutionSummary);
        Assert.Null(ticket.ResolvedAt);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Empty(persistence.Histories);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_BlankSummary_ThrowsValidationWithoutTimeOrPersistence(string? summary)
    {
        var actor = Technician();
        var persistence = new RecordingPersistence(
            TicketInStatus(actor.UserId, TicketStatus.InProgress));
        var time = new CountingTimeProvider(OccurredAt);
        var useCase = UseCase(persistence, time);

        var exception = await Assert.ThrowsAsync<TicketTechnicalOperationValidationException>(() =>
            useCase.ResolveAsync(persistence.Ticket!.Id, summary!, actor));

        Assert.Equal("resolutionSummary", exception.ParamName);
        Assert.Equal(0, time.CallCount);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Empty(persistence.Histories);
    }

    [Fact]
    public async Task ResolveAsync_OverLimitSummary_ThrowsValidationWithoutPersistence()
    {
        var actor = Technician();
        var persistence = new RecordingPersistence(
            TicketInStatus(actor.UserId, TicketStatus.InProgress));
        var useCase = UseCase(persistence);

        var exception = await Assert.ThrowsAsync<TicketTechnicalOperationValidationException>(() =>
            useCase.ResolveAsync(
                persistence.Ticket!.Id,
                new string('x', Ticket.MaximumResolutionSummaryLength + 1),
                actor));

        Assert.Equal("resolutionSummary", exception.ParamName);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Theory]
    [InlineData(TicketPriority.Low)]
    [InlineData(TicketPriority.High)]
    public async Task ChangePriorityAsync_ValidPriority_PersistsExactHistory(TicketPriority priority)
    {
        var actor = Technician();
        var ticket = TicketInStatus(actor.UserId, TicketStatus.Assigned);
        var persistence = new RecordingPersistence(ticket);
        var useCase = UseCase(persistence);

        var result = await useCase.ChangePriorityAsync(ticket.Id, priority, actor);

        Assert.Equal(TicketTechnicalOperationOutcome.Success, result.Outcome);
        Assert.Equal(priority, result.Ticket?.Priority);
        AssertHistory(
            Assert.Single(persistence.Histories),
            ticket.Id,
            actor.UserId,
            TicketHistoryEventType.PriorityChanged,
            "Medium",
            priority.ToString());
    }

    [Fact]
    public async Task ChangePriorityAsync_SamePriority_ReturnsConflictWithoutFakeHistory()
    {
        var actor = Technician();
        var persistence = new RecordingPersistence(
            TicketInStatus(actor.UserId, TicketStatus.Assigned));
        var useCase = UseCase(persistence);

        var result = await useCase.ChangePriorityAsync(
            persistence.Ticket!.Id,
            TicketPriority.Medium,
            actor);

        Assert.Equal(TicketTechnicalOperationOutcome.InvalidState, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Empty(persistence.Histories);
    }

    [Fact]
    public async Task ChangePriorityAsync_UndefinedPriority_ThrowsValidationWithoutPersistence()
    {
        var actor = Technician();
        var persistence = new RecordingPersistence(
            TicketInStatus(actor.UserId, TicketStatus.Assigned));
        var useCase = UseCase(persistence);

        var exception = await Assert.ThrowsAsync<TicketTechnicalOperationValidationException>(() =>
            useCase.ChangePriorityAsync(persistence.Ticket!.Id, (TicketPriority)999, actor));

        Assert.Equal("priority", exception.ParamName);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Fact]
    public async Task ChangePriorityAsync_MissingPriority_ThrowsValidationWithoutPersistence()
    {
        var actor = Technician();
        var persistence = new RecordingPersistence(
            TicketInStatus(actor.UserId, TicketStatus.Assigned));
        var useCase = UseCase(persistence);

        var exception = await Assert.ThrowsAsync<TicketTechnicalOperationValidationException>(() =>
            useCase.ChangePriorityAsync(persistence.Ticket!.Id, null, actor));

        Assert.Equal("priority", exception.ParamName);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Empty(persistence.Histories);
    }

    [Fact]
    public async Task ChangePriorityAsync_ResolvedTicket_ReturnsStateConflict()
    {
        var actor = Technician();
        var persistence = new RecordingPersistence(
            TicketInStatus(actor.UserId, TicketStatus.Resolved));
        var useCase = UseCase(persistence);

        var result = await useCase.ChangePriorityAsync(
            persistence.Ticket!.Id,
            TicketPriority.High,
            actor);

        Assert.Equal(TicketTechnicalOperationOutcome.InvalidState, result.Outcome);
        Assert.Equal(0, persistence.PersistCallCount);
    }

    [Fact]
    public async Task StartWorkAsync_ConcurrencyConflict_ReturnsDeliberateConflict()
    {
        var actor = Technician();
        var persistence = new RecordingPersistence(
            TicketInStatus(actor.UserId, TicketStatus.Assigned),
            TicketAssignmentPersistenceOutcome.ConcurrencyConflict);
        var useCase = UseCase(persistence);

        var result = await useCase.StartWorkAsync(persistence.Ticket!.Id, actor);

        Assert.Equal(TicketTechnicalOperationOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Equal(1, persistence.PersistCallCount);
    }

    [Fact]
    public async Task StartWorkAsync_UnrelatedPersistenceFailure_Propagates()
    {
        var actor = Technician();
        var expected = new InvalidOperationException("unexpected database failure");
        var persistence = new RecordingPersistence(
            TicketInStatus(actor.UserId, TicketStatus.Assigned),
            exception: expected);
        var useCase = UseCase(persistence);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            useCase.StartWorkAsync(persistence.Ticket!.Id, actor));

        Assert.Same(expected, exception);
    }

    [Fact]
    public async Task StartWorkAsync_PreCancelled_DoesNotLoadMutateReadTimeOrPersist()
    {
        var actor = Technician();
        var ticket = TicketInStatus(actor.UserId, TicketStatus.Assigned);
        var persistence = new RecordingPersistence(ticket);
        var time = new CountingTimeProvider(OccurredAt);
        var useCase = UseCase(persistence, time);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.StartWorkAsync(ticket.Id, actor, source.Token));

        Assert.Equal(TicketStatus.Assigned, ticket.Status);
        Assert.Equal(0, persistence.FindCallCount);
        Assert.Equal(0, persistence.PersistCallCount);
        Assert.Equal(0, time.CallCount);
    }

    private static TicketTechnicalOperationsUseCase UseCase(
        RecordingPersistence persistence,
        CountingTimeProvider? timeProvider = null)
    {
        return new TicketTechnicalOperationsUseCase(
            persistence,
            timeProvider ?? new CountingTimeProvider(OccurredAt));
    }

    private static Task<TicketTechnicalOperationResult> ExecuteAsync(
        TicketTechnicalOperationsUseCase useCase,
        string operation,
        Guid ticketId,
        RequestActor actor)
    {
        return operation switch
        {
            "start" => useCase.StartWorkAsync(ticketId, actor),
            "wait" => useCase.WaitAsync(ticketId, actor),
            "resume" => useCase.ResumeAsync(ticketId, actor),
            "resolve" => useCase.ResolveAsync(ticketId, "Resolved.", actor),
            "priority" => useCase.ChangePriorityAsync(ticketId, TicketPriority.High, actor),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static RequestActor Technician() => new(Guid.NewGuid(), UserRole.Technician);

    private static Ticket OpenTicket()
    {
        return Ticket.Create(
            "VPN unavailable",
            "The VPN connection fails consistently.",
            TicketCategory.Network,
            TicketPriority.Medium,
            Guid.NewGuid(),
            CreatedAt);
    }

    private static Ticket TicketInStatus(Guid technicianId, TicketStatus status)
    {
        var ticket = OpenTicket();
        if (status == TicketStatus.Open)
        {
            return ticket;
        }

        ticket.Assign(technicianId, CreatedAt.AddMinutes(1));
        if (status == TicketStatus.Assigned)
        {
            return ticket;
        }

        ticket.StartWork(CreatedAt.AddMinutes(2));
        if (status == TicketStatus.InProgress)
        {
            return ticket;
        }

        ticket.Wait(CreatedAt.AddMinutes(3));
        if (status == TicketStatus.Waiting)
        {
            return ticket;
        }

        ticket.Resume(CreatedAt.AddMinutes(4));
        ticket.Resolve("Resolved.", CreatedAt.AddMinutes(5));
        if (status == TicketStatus.Resolved)
        {
            return ticket;
        }

        ticket.Close(CreatedAt.AddMinutes(6));
        return ticket;
    }

    private static void AssertSuccess(
        TicketTechnicalOperationResult result,
        Ticket ticket,
        TicketStatus status)
    {
        Assert.Equal(TicketTechnicalOperationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Ticket);
        Assert.Equal(ticket.Id, result.Ticket.Id);
        Assert.Equal(status, result.Ticket.Status);
        Assert.Equal(OccurredAt, result.Ticket.UpdatedAt);
    }

    private static void AssertHistory(
        TicketHistory history,
        Guid ticketId,
        Guid actorId,
        TicketHistoryEventType eventType,
        string? oldValue,
        string? newValue)
    {
        Assert.Equal(ticketId, history.TicketId);
        Assert.Equal<Guid?>(actorId, history.PerformedByUserId);
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
