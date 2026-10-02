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
        var useCase = CreateUseCase(persistence);
        using var cancellationSource = new CancellationTokenSource();
        var command = ValidCommand();

        var result = await useCase.ExecuteAsync(command, cancellationSource.Token);

        var persistedTicket = Assert.Single(persistence.PersistedTickets);
        Assert.Equal(result.Id, persistedTicket.Id);
        Assert.Equal(command.Title, persistedTicket.Title);
        Assert.Equal(command.Description, persistedTicket.Description);
        Assert.Equal(command.Category, persistedTicket.Category);
        Assert.Equal(command.Priority, persistedTicket.Priority);
        Assert.Equal(command.CreatedByUserId, persistedTicket.CreatedByUserId);
        Assert.Equal(CurrentTime, persistedTicket.CreatedAt);
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
        Assert.Empty(persistence.PersistedTickets);
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

        Assert.Empty(persistence.PersistedTickets);
    }

    private static CreateTicketUseCase CreateUseCase(ICreateTicketPersistence persistence)
    {
        return new CreateTicketUseCase(persistence, new FixedTimeProvider(CurrentTime));
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
        public List<Ticket> PersistedTickets { get; } = [];

        public CancellationToken CancellationToken { get; private set; }

        public Task PersistAsync(Ticket ticket, CancellationToken cancellationToken)
        {
            PersistedTickets.Add(ticket);
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => currentTime;
    }
}
