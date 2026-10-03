using ServiceDesk.Core.Application.Tickets.CreateTicket;
using ServiceDesk.Core.Entities;

namespace ServiceDesk.Infrastructure.Persistence;

public sealed class CreateTicketPersistence(ServiceDeskDbContext dbContext) : ICreateTicketPersistence
{
    public async Task PersistAsync(
        Ticket ticket,
        TicketHistory history,
        CancellationToken cancellationToken)
    {
        dbContext.Tickets.Add(ticket);
        dbContext.TicketHistory.Add(history);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
