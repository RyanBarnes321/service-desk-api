namespace ServiceDesk.Core.Application.Tickets.Comments;

public sealed record ListTicketCommentsResult(
    IReadOnlyList<TicketCommentResult> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
