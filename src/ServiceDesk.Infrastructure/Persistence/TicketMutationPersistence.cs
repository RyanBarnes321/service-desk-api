using Microsoft.EntityFrameworkCore;
using ServiceDesk.Core.Application.Tickets.Assignment;
using ServiceDesk.Core.Entities;

namespace ServiceDesk.Infrastructure.Persistence;

public sealed class TicketMutationPersistence(ServiceDeskDbContext dbContext)
    : ITicketMutationPersistence
{
    public Task<Ticket?> FindTrackedAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        return dbContext.Tickets.SingleOrDefaultAsync(
            ticket => ticket.Id == ticketId,
            cancellationToken);
    }

    public async Task<TicketMutationPersistenceOutcome> PersistAsync(
        Ticket ticket,
        IReadOnlyCollection<TicketHistory> histories,
        CancellationToken cancellationToken)
    {
        dbContext.TicketHistory.AddRange(histories);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return TicketMutationPersistenceOutcome.Persisted;
        }
        catch (DbUpdateConcurrencyException)
        {
            return TicketMutationPersistenceOutcome.ConcurrencyConflict;
        }
    }
}
