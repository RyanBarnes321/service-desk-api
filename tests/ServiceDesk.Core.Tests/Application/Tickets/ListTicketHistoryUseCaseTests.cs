using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Tickets;
using ServiceDesk.Core.Application.Tickets.History;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Tests.Application.Tickets;

public class ListTicketHistoryUseCaseTests
{
    private static readonly Guid TicketId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EmployeeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task ExecuteAsync_DefaultsAndEmployeeVisibility_DelegateExactly()
    {
        var expected = new ListTicketHistoryResult([], 1, 20, 0, 0);
        var query = new RecordingQuery(expected);

        var result = await new ListTicketHistoryUseCase(query).ExecuteAsync(
            TicketId,
            new ListTicketHistoryRequest(),
            new RequestActor(EmployeeId, UserRole.Employee));

        Assert.Same(expected, result);
        Assert.Equal(1, query.CallCount);
        Assert.Equal(TicketId, query.TicketId);
        Assert.Equal(new ListTicketHistoryRequest(1, 20), query.Request);
        Assert.Equal(EmployeeId, query.Visibility?.CreatedByUserId);
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Administrator)]
    public async Task ExecuteAsync_PrivilegedActor_UsesBroadVisibility(UserRole role)
    {
        var query = new RecordingQuery(new([], 1, 20, 0, 0));

        await new ListTicketHistoryUseCase(query).ExecuteAsync(
            TicketId,
            new ListTicketHistoryRequest(),
            new RequestActor(Guid.NewGuid(), role));

        Assert.Null(query.Visibility?.CreatedByUserId);
    }

    [Fact]
    public async Task ExecuteAsync_InaccessibleOrMissingTicket_ReturnsNull()
    {
        var query = new RecordingQuery(null);

        var result = await new ListTicketHistoryUseCase(query).ExecuteAsync(
            TicketId,
            new ListTicketHistoryRequest(),
            new RequestActor(EmployeeId, UserRole.Employee));

        Assert.Null(result);
        Assert.Equal(1, query.CallCount);
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public async Task ExecuteAsync_InvalidPagination_ThrowsWithoutQuery(
        int page,
        int pageSize,
        string parameterName)
    {
        var query = new RecordingQuery(new([], 1, 20, 0, 0));

        var exception = await Assert.ThrowsAsync<TicketHistoryValidationException>(() =>
            new ListTicketHistoryUseCase(query).ExecuteAsync(
                TicketId,
                new ListTicketHistoryRequest(page, pageSize),
                new RequestActor(EmployeeId, UserRole.Employee)));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_CustomPaginationAndCancellationToken_DelegateExactly()
    {
        var query = new RecordingQuery(new([], 3, 50, 0, 0));
        using var source = new CancellationTokenSource();

        await new ListTicketHistoryUseCase(query).ExecuteAsync(
            TicketId,
            new ListTicketHistoryRequest(3, 50),
            new RequestActor(Guid.NewGuid(), UserRole.Administrator),
            source.Token);

        Assert.Equal(new ListTicketHistoryRequest(3, 50), query.Request);
        Assert.Equal(source.Token, query.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_PreCancelled_DoesNotQuery()
    {
        var query = new RecordingQuery(new([], 1, 20, 0, 0));
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ListTicketHistoryUseCase(query).ExecuteAsync(
                TicketId,
                new ListTicketHistoryRequest(),
                new RequestActor(EmployeeId, UserRole.Employee),
                source.Token));

        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public void UndefinedRole_FailsClosedAtActorConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RequestActor(Guid.NewGuid(), (UserRole)999));
    }

    private sealed class RecordingQuery(ListTicketHistoryResult? result)
        : IListTicketHistoryQuery
    {
        public int CallCount { get; private set; }
        public Guid TicketId { get; private set; }
        public ListTicketHistoryRequest? Request { get; private set; }
        public TicketVisibilityScope? Visibility { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<ListTicketHistoryResult?> ListAsync(
            Guid ticketId,
            ListTicketHistoryRequest request,
            TicketVisibilityScope visibility,
            CancellationToken cancellationToken)
        {
            CallCount++;
            TicketId = ticketId;
            Request = request;
            Visibility = visibility;
            CancellationToken = cancellationToken;
            return Task.FromResult(result);
        }
    }
}
