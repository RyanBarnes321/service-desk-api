namespace ServiceDesk.Core.Application.Tickets.ResolutionReview;

public enum TicketResolutionReviewOutcome
{
    Success,
    Forbidden,
    TicketNotFound,
    InvalidState,
    ConcurrencyConflict
}
