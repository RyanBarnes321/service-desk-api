namespace ServiceDesk.Api.Contracts.Tickets;

public sealed record ListTicketHistoryResponse(
    IReadOnlyList<TicketHistoryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
