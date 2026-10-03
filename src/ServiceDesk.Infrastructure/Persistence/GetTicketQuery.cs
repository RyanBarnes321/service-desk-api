using Microsoft.EntityFrameworkCore;
using ServiceDesk.Core.Application.Tickets;
using ServiceDesk.Core.Application.Tickets.GetTicket;

namespace ServiceDesk.Infrastructure.Persistence;

public sealed class GetTicketQuery(ServiceDeskDbContext dbContext) : IGetTicketQuery
{
    public Task<GetTicketResult?> FindAsync(
        Guid id,
        TicketVisibilityScope visibility,
        CancellationToken cancellationToken)
    {
        var tickets = dbContext.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.Id == id);

        if (visibility.CreatedByUserId is { } createdByUserId)
        {
            tickets = tickets.Where(ticket => ticket.CreatedByUserId == createdByUserId);
        }

        return tickets
            .Select(ticket => new GetTicketResult(
                ticket.Id,
                ticket.Title,
                ticket.Description,
                ticket.Category,
                ticket.Priority,
                ticket.Status,
                ticket.CreatedByUserId,
                ticket.AssignedTechnicianId,
                ticket.ResolutionSummary,
                ticket.CreatedAt,
                ticket.UpdatedAt,
                ticket.ResolvedAt,
                ticket.ClosedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
