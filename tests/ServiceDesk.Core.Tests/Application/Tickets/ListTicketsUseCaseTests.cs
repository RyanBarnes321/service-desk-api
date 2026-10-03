using ServiceDesk.Core.Application.Tickets.ListTickets;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Tests.Application.Tickets;

public class ListTicketsUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_DefaultRequest_DelegatesDefaults()
    {
        var query = new RecordingQuery();
        var useCase = new ListTicketsUseCase(query);

        await useCase.ExecuteAsync(new ListTicketsRequest());

        Assert.NotNull(query.Request);
        Assert.Equal(1, query.Request.Page);
        Assert.Equal(20, query.Request.PageSize);
        Assert.Equal(TicketSortField.CreatedAt, query.Request.SortField);
        Assert.Equal(TicketSortDirection.Desc, query.Request.SortDirection);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidPage_ThrowsWithoutQuerying()
    {
        var query = new RecordingQuery();
        var useCase = new ListTicketsUseCase(query);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => useCase.ExecuteAsync(new ListTicketsRequest(Page: 0)));

        Assert.Equal("page", exception.ParamName);
        Assert.Equal(0, query.CallCount);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("priority")]
    [InlineData("category")]
    public async Task ExecuteAsync_UndefinedFilterEnum_ThrowsWithoutQuerying(string parameterName)
    {
        var query = new RecordingQuery();
        var useCase = new ListTicketsUseCase(query);
        var request = parameterName switch
        {
            "status" => new ListTicketsRequest(Status: (TicketStatus)999),
            "priority" => new ListTicketsRequest(Priority: (TicketPriority)999),
            "category" => new ListTicketsRequest(Category: (TicketCategory)999),
            _ => throw new InvalidOperationException("Unexpected test parameter.")
        };

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => useCase.ExecuteAsync(request));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.Equal(0, query.CallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ExecuteAsync_InvalidPageSize_ThrowsWithoutQuerying(int pageSize)
    {
        var query = new RecordingQuery();
        var useCase = new ListTicketsUseCase(query);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => useCase.ExecuteAsync(new ListTicketsRequest(PageSize: pageSize)));

        Assert.Equal("pageSize", exception.ParamName);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_InvertedCreatedRange_ThrowsWithoutQuerying()
    {
        var query = new RecordingQuery();
        var useCase = new ListTicketsUseCase(query);
        var from = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            useCase.ExecuteAsync(new ListTicketsRequest(
                CreatedFrom: from,
                CreatedTo: from.AddTicks(-1))));

        Assert.Equal("createdFrom", exception.ParamName);
        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhitespaceSearch_NormalizesToNull()
    {
        var query = new RecordingQuery();
        var useCase = new ListTicketsUseCase(query);

        await useCase.ExecuteAsync(new ListTicketsRequest(Search: " \t "));

        Assert.NotNull(query.Request);
        Assert.Null(query.Request.Search);
    }

    [Fact]
    public async Task ExecuteAsync_PreCancelled_ThrowsWithoutQuerying()
    {
        var query = new RecordingQuery();
        var useCase = new ListTicketsUseCase(query);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(new ListTicketsRequest(), cancellationSource.Token));

        Assert.Equal(0, query.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_DelegatesNormalizedValuesAndTokenExactlyOnce()
    {
        var query = new RecordingQuery();
        var useCase = new ListTicketsUseCase(query);
        using var cancellationSource = new CancellationTokenSource();
        var createdFrom = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var createdTo = createdFrom.AddDays(1);
        var technicianId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var request = new ListTicketsRequest(
            TicketStatus.Assigned,
            TicketPriority.High,
            TicketCategory.Network,
            technicianId,
            creatorId,
            createdFrom,
            createdTo,
            "  VPN issue  ",
            2,
            50,
            TicketSortField.CreatedAt,
            TicketSortDirection.Asc);

        var result = await useCase.ExecuteAsync(request, cancellationSource.Token);

        Assert.Same(query.Result, result);
        Assert.Equal(1, query.CallCount);
        Assert.Equal(request with { Search = "VPN issue" }, query.Request);
        Assert.Equal(cancellationSource.Token, query.CancellationToken);
    }

    private sealed class RecordingQuery : IListTicketsQuery
    {
        public ListTicketsResult Result { get; } = new([], 1, 20, 0, 0);

        public int CallCount { get; private set; }

        public ListTicketsRequest? Request { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<ListTicketsResult> ListAsync(
            ListTicketsRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            CancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }
    }
}
