using ServiceDesk.Core.Application.Authentication;

namespace ServiceDesk.Core.Application.Tickets.Comments;

public sealed class ListTicketCommentsUseCase(IListTicketCommentsQuery query)
{
    public Task<ListTicketCommentsResult?> ExecuteAsync(
        Guid ticketId,
        ListTicketCommentsRequest request,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        if (ticketId == Guid.Empty)
        {
            throw new TicketCommentsValidationException(
                "Ticket ID cannot be empty.",
                nameof(ticketId));
        }

        if (request.Page < 1)
        {
            throw new TicketCommentsValidationException(
                "Page must be at least 1.",
                "page");
        }

        if (request.PageSize < 1 || request.PageSize > ListTicketCommentsRequest.MaximumPageSize)
        {
            throw new TicketCommentsValidationException(
                $"Page size must be between 1 and {ListTicketCommentsRequest.MaximumPageSize}.",
                "pageSize");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return query.ListAsync(
            ticketId,
            request,
            TicketVisibilityScope.For(actor),
            cancellationToken);
    }
}
