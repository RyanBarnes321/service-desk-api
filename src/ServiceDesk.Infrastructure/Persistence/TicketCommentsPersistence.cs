using Microsoft.EntityFrameworkCore;
using ServiceDesk.Core.Application.Tickets;
using ServiceDesk.Core.Application.Tickets.Comments;
using ServiceDesk.Core.Entities;

namespace ServiceDesk.Infrastructure.Persistence;

public sealed class TicketCommentsPersistence(ServiceDeskDbContext dbContext)
    : ICreateTicketCommentPersistence, IListTicketCommentsQuery
{
    public Task<CreateTicketCommentTarget?> FindVisibleTicketAsync(
        Guid ticketId,
        TicketVisibilityScope visibility,
        CancellationToken cancellationToken)
    {
        var tickets = VisibleTickets(visibility)
            .Where(ticket => ticket.Id == ticketId);

        return tickets
            .Select(ticket => new CreateTicketCommentTarget(ticket.Id, ticket.Status))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task PersistAsync(
        TicketComment comment,
        CancellationToken cancellationToken)
    {
        dbContext.TicketComments.Add(comment);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ListTicketCommentsResult?> ListAsync(
        Guid ticketId,
        ListTicketCommentsRequest request,
        TicketVisibilityScope visibility,
        CancellationToken cancellationToken)
    {
        var isVisible = await VisibleTickets(visibility)
            .AnyAsync(ticket => ticket.Id == ticketId, cancellationToken);
        if (!isVisible)
        {
            return null;
        }

        var comments = dbContext.TicketComments
            .AsNoTracking()
            .Where(comment => comment.TicketId == ticketId);
        var totalCount = await comments.CountAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var offset = (long)(request.Page - 1) * request.PageSize;
        IReadOnlyList<TicketCommentResult> items = offset > int.MaxValue
            ? []
            : await comments
                .OrderBy(comment => comment.CreatedAt)
                .ThenBy(comment => comment.Id)
                .Skip((int)offset)
                .Take(request.PageSize)
                .Select(comment => new TicketCommentResult(
                    comment.Id,
                    comment.TicketId,
                    comment.AuthorUserId,
                    comment.Content,
                    comment.CreatedAt))
                .ToArrayAsync(cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)request.PageSize);

        return new ListTicketCommentsResult(
            items,
            request.Page,
            request.PageSize,
            totalCount,
            totalPages);
    }

    private IQueryable<Ticket> VisibleTickets(TicketVisibilityScope visibility)
    {
        var tickets = dbContext.Tickets.AsNoTracking();
        if (visibility.CreatedByUserId is { } creatorId)
        {
            tickets = tickets.Where(ticket => ticket.CreatedByUserId == creatorId);
        }

        return tickets;
    }
}
