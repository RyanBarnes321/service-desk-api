namespace ServiceDesk.Api.Contracts.Tickets;

public sealed record TicketCommentResponse(
    Guid Id,
    Guid TicketId,
    Guid AuthorUserId,
    string Content,
    DateTimeOffset CreatedAt);
