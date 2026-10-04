namespace ServiceDesk.Api.Contracts.Tickets;

public sealed class ListTicketCommentsQueryRequest
{
    public int? Page { get; init; }

    public int? PageSize { get; init; }
}
