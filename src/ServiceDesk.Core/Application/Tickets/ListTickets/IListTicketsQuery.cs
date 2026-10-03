namespace ServiceDesk.Core.Application.Tickets.ListTickets;

public interface IListTicketsQuery
{
    Task<ListTicketsResult> ListAsync(
        ListTicketsRequest request,
        CancellationToken cancellationToken);
}
