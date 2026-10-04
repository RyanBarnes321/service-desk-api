namespace ServiceDesk.Core.Application.Tickets.Comments;

public sealed record TicketCommentResult(
    Guid Id,
    Guid TicketId,
    Guid AuthorUserId,
    string Content,
    DateTimeOffset CreatedAt);
