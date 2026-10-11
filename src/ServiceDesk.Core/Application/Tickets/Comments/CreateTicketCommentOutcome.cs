namespace ServiceDesk.Core.Application.Tickets.Comments;

public enum CreateTicketCommentOutcome
{
    Success,
    TicketNotFound,
    TicketClosed
}

public sealed record CreateTicketCommentResult(
    CreateTicketCommentOutcome Outcome,
    TicketCommentResult? Comment = null);
