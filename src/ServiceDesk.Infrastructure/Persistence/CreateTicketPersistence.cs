using ServiceDesk.Core.Application.Tickets.CreateTicket;
using ServiceDesk.Core.Entities;

namespace ServiceDesk.Infrastructure.Persistence;

public sealed class CreateTicketPersistence(ServiceDeskDbContext dbContext) : ICreateTicketPersistence
{
    public async Task PersistAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        await dbContext.Tickets.AddAsync(ticket, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
