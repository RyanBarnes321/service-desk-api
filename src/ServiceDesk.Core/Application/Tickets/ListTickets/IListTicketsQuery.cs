using ServiceDesk.Core.Application.Tickets;

namespace ServiceDesk.Core.Application.Tickets.ListTickets;

public interface IListTicketsQuery
{
    Task<ListTicketsResult> ListAsync(
        ListTicketsRequest request,
        TicketVisibilityScope visibility,
        CancellationToken cancellationToken);
}
