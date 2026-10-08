using Microsoft.EntityFrameworkCore;
using ServiceDesk.Core.Application.Tickets;
using ServiceDesk.Core.Application.Tickets.History;

namespace ServiceDesk.Infrastructure.Persistence;

public sealed class ListTicketHistoryQuery(ServiceDeskDbContext dbContext)
    : IListTicketHistoryQuery
{
    public async Task<ListTicketHistoryResult?> ListAsync(
        Guid ticketId,
        ListTicketHistoryRequest request,
        TicketVisibilityScope visibility,
        CancellationToken cancellationToken)
    {
        var tickets = dbContext.Tickets.AsNoTracking();
        if (visibility.CreatedByUserId is { } creatorId)
        {
            tickets = tickets.Where(ticket => ticket.CreatedByUserId == creatorId);
        }

        if (!await tickets.AnyAsync(ticket => ticket.Id == ticketId, cancellationToken))
        {
            return null;
        }

        var history = dbContext.TicketHistory
            .AsNoTracking()
            .Where(item => item.TicketId == ticketId);
        var totalCount = await history.CountAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var offset = (long)(request.Page - 1) * request.PageSize;
        IReadOnlyList<TicketHistoryItem> items = offset > int.MaxValue
            ? []
            : await history
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .Skip((int)offset)
                .Take(request.PageSize)
                .Select(item => new TicketHistoryItem(
                    item.Id,
                    item.TicketId,
                    item.PerformedByUserId,
                    item.EventType,
                    item.OldValue,
                    item.NewValue,
                    item.CreatedAt))
                .ToArrayAsync(cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)request.PageSize);

        return new ListTicketHistoryResult(
            items,
            request.Page,
            request.PageSize,
            totalCount,
            totalPages);
    }
}
