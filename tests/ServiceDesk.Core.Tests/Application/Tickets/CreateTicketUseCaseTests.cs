using ServiceDesk.Core.Application.Tickets.CreateTicket;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Tests.Application.Tickets;

public class CreateTicketUseCaseTests
{
    private static readonly DateTimeOffset CurrentTime = new(2026, 10, 2, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_ValidCommand_CreatesAndReturnsTicket()
    {
        var persistence = new RecordingPersistence();
        var useCase = CreateUseCase(persistence);
        var creatorId = Guid.NewGuid();
        var command = new CreateTicketCommand(
            "Cannot connect to VPN",
            "The VPN client times out during connection.",
            TicketCategory.Network,
            TicketPriority.High,
            creatorId);

        var result = await useCase.ExecuteAsync(command);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(command.Title, result.Title);
        Assert.Equal(command.Description, result.Description);
        Assert.Equal(command.Category, result.Category);
        Assert.Equal(command.Priority, result.Priority);
        Assert.Equal(TicketStatus.Open, result.Status);
        Assert.Equal(creatorId, result.CreatedByUserId);
        Assert.Equal(CurrentTime, result.CreatedAt);
    }

    [Fact]
    public async Task ExecuteAsync_ValidCommand_DelegatesTicketAndCancellationTokenExactlyOnce()
    {
        var persistence = new RecordingPersistence();
        var timeProvider = new RecordingTimeProvider(CurrentTime);
        var useCase = new CreateTicketUseCase(persistence, timeProvider);
        using var cancellationSource = new CancellationTokenSource();
        var command = ValidCommand();

        var result = await useCase.ExecuteAsync(command, cancellationSource.Token);

        Assert.Equal(1, persistence.CallCount);
        Assert.Equal(1, timeProvider.GetUtcNowCallCount);
        var persistedTicket = Assert.IsType<Ticket>(persistence.PersistedTicket);
        var persistedHistory = Assert.IsType<TicketHistory>(persistence.PersistedHistory);
        Assert.Equal(result.Id, persistedTicket.Id);
        Assert.Equal(command.Title, persistedTicket.Title);
        Assert.Equal(command.Description, persistedTicket.Description);
        Assert.Equal(command.Category, persistedTicket.Category);
        Assert.Equal(command.Priority, persistedTicket.Priority);
        Assert.Equal(command.CreatedByUserId, persistedTicket.CreatedByUserId);
        Assert.Equal(CurrentTime, persistedTicket.CreatedAt);
        Assert.NotEqual(Guid.Empty, persistedHistory.Id);
        Assert.Equal(persistedTicket.Id, persistedHistory.TicketId);
        Assert.Equal<Guid?>(command.CreatedByUserId, persistedHistory.PerformedByUserId);
        Assert.Equal(TicketHistoryEventType.Created, persistedHistory.EventType);
        Assert.Null(persistedHistory.OldValue);
        Assert.Null(persistedHistory.NewValue);
        Assert.Equal(persistedTicket.CreatedAt, persistedHistory.CreatedAt);
        Assert.Equal(cancellationSource.Token, persistence.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_DomainInvalidTitle_PropagatesExceptionWithoutPersistence()
    {
        var persistence = new RecordingPersistence();
        var useCase = CreateUseCase(persistence);
        var command = ValidCommand() with { Title = " " };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => useCase.ExecuteAsync(command));

        Assert.Equal("title", exception.ParamName);
        Assert.Equal(0, persistence.CallCount);
        Assert.Null(persistence.PersistedTicket);
        Assert.Null(persistence.PersistedHistory);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledBeforeExecution_ThrowsWithoutPersistence()
    {
        var persistence = new RecordingPersistence();
        var useCase = CreateUseCase(persistence);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(ValidCommand(), cancellationSource.Token));

        Assert.Equal(0, persistence.CallCount);
        Assert.Null(persistence.PersistedTicket);
        Assert.Null(persistence.PersistedHistory);
    }

    private static CreateTicketUseCase CreateUseCase(ICreateTicketPersistence persistence)
    {
        return new CreateTicketUseCase(persistence, new RecordingTimeProvider(CurrentTime));
    }

    private static CreateTicketCommand ValidCommand()
    {
        return new CreateTicketCommand(
            "Cannot connect to VPN",
            "The VPN client times out during connection.",
            TicketCategory.Network,
            TicketPriority.High,
            Guid.NewGuid());
    }

    private sealed class RecordingPersistence : ICreateTicketPersistence
    {
        public int CallCount { get; private set; }

        public Ticket? PersistedTicket { get; private set; }

        public TicketHistory? PersistedHistory { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task PersistAsync(
            Ticket ticket,
            TicketHistory history,
            CancellationToken cancellationToken)
        {
            CallCount++;
            PersistedTicket = ticket;
            PersistedHistory = history;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public int GetUtcNowCallCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            GetUtcNowCallCount++;
            return currentTime;
        }
    }
}
