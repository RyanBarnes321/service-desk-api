namespace ServiceDesk.Api.Contracts.Tickets;

public sealed record ListTicketsResponse(
    IReadOnlyList<ListTicketResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
