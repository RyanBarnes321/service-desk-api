using ServiceDesk.Core.Entities;

namespace ServiceDesk.Core.Application.Tickets.Assignment;

public interface ITicketMutationPersistence
{
    Task<Ticket?> FindTrackedAsync(Guid ticketId, CancellationToken cancellationToken);

    Task<TicketMutationPersistenceOutcome> PersistAsync(
        Ticket ticket,
        IReadOnlyCollection<TicketHistory> histories,
        CancellationToken cancellationToken);
}
