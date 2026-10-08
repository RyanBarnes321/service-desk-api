using ServiceDesk.Core.Application.Authentication;

namespace ServiceDesk.Core.Application.Tickets.History;

public sealed class ListTicketHistoryUseCase(IListTicketHistoryQuery query)
{
    public Task<ListTicketHistoryResult?> ExecuteAsync(
        Guid ticketId,
        ListTicketHistoryRequest request,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        if (ticketId == Guid.Empty)
        {
            throw new TicketHistoryValidationException(
                "Ticket ID cannot be empty.",
                nameof(ticketId));
        }

        if (request.Page < 1)
        {
            throw new TicketHistoryValidationException(
                "Page must be at least 1.",
                "page");
        }

        if (request.PageSize < 1 || request.PageSize > ListTicketHistoryRequest.MaximumPageSize)
        {
            throw new TicketHistoryValidationException(
                $"Page size must be between 1 and {ListTicketHistoryRequest.MaximumPageSize}.",
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
