using ServiceDesk.Core.Entities;

namespace ServiceDesk.Core.Application.Tickets.CreateTicket;

public interface ICreateTicketPersistence
{
    Task PersistAsync(Ticket ticket, CancellationToken cancellationToken);
}
