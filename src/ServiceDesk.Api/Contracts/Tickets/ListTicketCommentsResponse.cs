namespace ServiceDesk.Api.Contracts.Tickets;

public sealed record ListTicketCommentsResponse(
    IReadOnlyList<TicketCommentResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
