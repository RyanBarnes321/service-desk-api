using ServiceDesk.Core.Application.Tickets;
using ServiceDesk.Core.Entities;

namespace ServiceDesk.Core.Application.Tickets.Comments;

public interface ICreateTicketCommentPersistence
{
    Task<CreateTicketCommentTarget?> FindVisibleTicketAsync(
        Guid ticketId,
        TicketVisibilityScope visibility,
        CancellationToken cancellationToken);

    Task PersistAsync(TicketComment comment, CancellationToken cancellationToken);
}
