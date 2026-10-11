namespace ServiceDesk.Api.Contracts.Tickets;

public sealed class ListTicketHistoryQueryRequest
{
    public int? Page { get; init; }

    public int? PageSize { get; init; }
}
