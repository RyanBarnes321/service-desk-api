using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Application.Tickets;

namespace ServiceDesk.Core.Application.Tickets.ListTickets;

public sealed class ListTicketsUseCase(IListTicketsQuery query)
{
    public Task<ListTicketsResult> ExecuteAsync(
        ListTicketsRequest request,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        Validate(request);

        var normalizedRequest = request with
        {
            Search = string.IsNullOrWhiteSpace(request.Search)
                ? null
                : request.Search.Trim()
        };
        var visibility = TicketVisibilityScope.For(actor);

        return query.ListAsync(normalizedRequest, visibility, cancellationToken);
    }

    private static void Validate(ListTicketsRequest request)
    {
        if (request.Status is { } status && !Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException("status", "Status must be a defined value.");
        }

        if (request.Priority is { } priority && !Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException("priority", "Priority must be a defined value.");
        }

        if (request.Category is { } category && !Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException("category", "Category must be a defined value.");
        }

        if (request.Page < 1)
        {
            throw new ArgumentOutOfRangeException("page", "Page must be at least 1.");
        }

        if (request.PageSize < 1 || request.PageSize > ListTicketsRequest.MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                "pageSize",
                $"Page size must be between 1 and {ListTicketsRequest.MaximumPageSize}.");
        }

        if (request.CreatedFrom > request.CreatedTo)
        {
            throw new ArgumentException(
                "Created-from must be earlier than or equal to created-to.",
                "createdFrom");
        }

        if (!Enum.IsDefined(request.SortField))
        {
            throw new ArgumentOutOfRangeException("sortField", "Sort field is not supported.");
        }

        if (!Enum.IsDefined(request.SortDirection))
        {
            throw new ArgumentOutOfRangeException("sortDirection", "Sort direction is not supported.");
        }
    }
}
