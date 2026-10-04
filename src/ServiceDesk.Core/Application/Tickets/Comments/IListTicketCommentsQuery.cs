using ServiceDesk.Core.Application.Tickets;

namespace ServiceDesk.Core.Application.Tickets.Comments;

public interface IListTicketCommentsQuery
{
    Task<ListTicketCommentsResult?> ListAsync(
        Guid ticketId,
        ListTicketCommentsRequest request,
        TicketVisibilityScope visibility,
        CancellationToken cancellationToken);
}
