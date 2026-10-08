namespace ServiceDesk.Core.Application.Tickets.History;

public sealed record ListTicketHistoryResult(
    IReadOnlyList<TicketHistoryItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
