namespace ServiceDesk.Core.Application.Tickets.ResolutionReview;

public sealed class TicketResolutionReviewValidationException(string message, string paramName)
    : ArgumentException(message, paramName);
