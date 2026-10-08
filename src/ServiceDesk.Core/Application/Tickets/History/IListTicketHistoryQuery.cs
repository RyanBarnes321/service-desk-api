using ServiceDesk.Core.Application.Tickets;

namespace ServiceDesk.Core.Application.Tickets.History;

public interface IListTicketHistoryQuery
{
    Task<ListTicketHistoryResult?> ListAsync(
        Guid ticketId,
        ListTicketHistoryRequest request,
        TicketVisibilityScope visibility,
        CancellationToken cancellationToken);
}
