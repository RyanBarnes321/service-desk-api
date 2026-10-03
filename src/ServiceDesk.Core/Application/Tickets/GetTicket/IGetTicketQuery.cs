using ServiceDesk.Core.Application.Tickets;

namespace ServiceDesk.Core.Application.Tickets.GetTicket;

public interface IGetTicketQuery
{
    Task<GetTicketResult?> FindAsync(
        Guid id,
        TicketVisibilityScope visibility,
        CancellationToken cancellationToken);
}
