using ServiceDesk.Core.Entities;

namespace ServiceDesk.Core.Application.Tickets.Assignment;

public interface ITicketAssignmentPersistence
{
    Task<Ticket?> FindTrackedAsync(Guid ticketId, CancellationToken cancellationToken);

    Task<TicketAssignmentPersistenceOutcome> PersistAsync(
        Ticket ticket,
        IReadOnlyCollection<TicketHistory> histories,
        CancellationToken cancellationToken);
}
