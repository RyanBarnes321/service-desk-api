namespace ServiceDesk.Core.Application.Tickets.ListTickets;

public sealed record ListTicketsResult(
    IReadOnlyList<ListTicketItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
