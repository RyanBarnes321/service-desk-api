using Microsoft.EntityFrameworkCore;
using ServiceDesk.Core.Application.Tickets.ListTickets;
using ServiceDesk.Core.Entities;

namespace ServiceDesk.Infrastructure.Persistence;

public sealed class ListTicketsQuery(ServiceDeskDbContext dbContext) : IListTicketsQuery
{
    public async Task<ListTicketsResult> ListAsync(
        ListTicketsRequest request,
        CancellationToken cancellationToken)
    {
        IQueryable<Ticket> tickets = dbContext.Tickets.AsNoTracking();

        if (request.Status is not null)
        {
            tickets = tickets.Where(ticket => ticket.Status == request.Status);
        }

        if (request.Priority is not null)
        {
            tickets = tickets.Where(ticket => ticket.Priority == request.Priority);
        }

        if (request.Category is not null)
        {
            tickets = tickets.Where(ticket => ticket.Category == request.Category);
        }

        if (request.AssignedTechnicianId is not null)
        {
            tickets = tickets.Where(ticket =>
                ticket.AssignedTechnicianId == request.AssignedTechnicianId);
        }

        if (request.CreatedByUserId is not null)
        {
            tickets = tickets.Where(ticket => ticket.CreatedByUserId == request.CreatedByUserId);
        }

        if (request.CreatedFrom is not null)
        {
            tickets = tickets.Where(ticket => ticket.CreatedAt >= request.CreatedFrom);
        }

        if (request.CreatedTo is not null)
        {
            tickets = tickets.Where(ticket => ticket.CreatedAt <= request.CreatedTo);
        }

        if (request.Search is not null)
        {
            var searchPattern = $"%{EscapeLikePattern(request.Search)}%";
            tickets = tickets.Where(ticket =>
                EF.Functions.ILike(ticket.Title, searchPattern, @"\") ||
                EF.Functions.ILike(ticket.Description, searchPattern, @"\"));
        }

        var totalCount = await tickets.CountAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var orderedTickets = request.SortDirection == TicketSortDirection.Asc
            ? tickets.OrderBy(ticket => ticket.CreatedAt).ThenBy(ticket => ticket.Id)
            : tickets.OrderByDescending(ticket => ticket.CreatedAt).ThenByDescending(ticket => ticket.Id);

        var offset = (long)(request.Page - 1) * request.PageSize;
        IReadOnlyList<ListTicketItem> items = offset > int.MaxValue
            ? []
            : await orderedTickets
                .Skip((int)offset)
                .Take(request.PageSize)
                .Select(ticket => new ListTicketItem(
                    ticket.Id,
                    ticket.Title,
                    ticket.Category,
                    ticket.Priority,
                    ticket.Status,
                    ticket.CreatedByUserId,
                    ticket.AssignedTechnicianId,
                    ticket.CreatedAt,
                    ticket.UpdatedAt))
                .ToArrayAsync(cancellationToken);

        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)request.PageSize);

        return new ListTicketsResult(
            items,
            request.Page,
            request.PageSize,
            totalCount,
            totalPages);
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }
}
