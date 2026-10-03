using ServiceDesk.Core.Application.Tickets.GetTicket;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Tests.Application.Tickets;

public class GetTicketUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ExistingTicket_ReturnsQueryResult()
    {
        var expected = TicketResult(Guid.NewGuid());
        var query = new RecordingQuery(expected);
        var useCase = new GetTicketUseCase(query);
        using var cancellationSource = new CancellationTokenSource();

        var result = await useCase.ExecuteAsync(expected.Id, cancellationSource.Token);

        Assert.Same(expected, result);
        Assert.Equal(expected.Id, query.QueriedId);
        Assert.Equal(cancellationSource.Token, query.CancellationToken);
        Assert.Equal(1, query.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_MissingTicket_ReturnsNull()
    {
        var query = new RecordingQuery(null);
        var useCase = new GetTicketUseCase(query);

        var result = await useCase.ExecuteAsync(Guid.NewGuid());

        Assert.Null(result);
        Assert.Equal(1, query.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyId_ThrowsWithoutQuerying()
    {
        var query = new RecordingQuery(null);
        var useCase = new GetTicketUseCase(query);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => useCase.ExecuteAsync(Guid.Empty));

        Assert.Equal("id", exception.ParamName);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledBeforeExecution_ThrowsWithoutQuerying()
    {
        var query = new RecordingQuery(null);
        var useCase = new GetTicketUseCase(query);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(Guid.NewGuid(), cancellationSource.Token));

        Assert.Equal(0, query.CallCount);
    }

    private static GetTicketResult TicketResult(Guid id)
    {
        var createdAt = new DateTimeOffset(2026, 10, 2, 13, 0, 0, TimeSpan.Zero);

        return new GetTicketResult(
            id,
            "Cannot connect to VPN",
            "The VPN client times out during connection.",
            TicketCategory.Network,
            TicketPriority.High,
            TicketStatus.Open,
            Guid.NewGuid(),
            null,
            null,
            createdAt,
            createdAt,
            null,
            null);
    }

    private sealed class RecordingQuery(GetTicketResult? result) : IGetTicketQuery
    {
        public int CallCount { get; private set; }

        public Guid QueriedId { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<GetTicketResult?> FindAsync(Guid id, CancellationToken cancellationToken)
        {
            CallCount++;
            QueriedId = id;
            CancellationToken = cancellationToken;
            return Task.FromResult(result);
        }
    }
}
