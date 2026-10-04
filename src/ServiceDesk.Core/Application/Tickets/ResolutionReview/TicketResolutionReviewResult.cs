using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.ResolutionReview;

public sealed record TicketResolutionReviewDetails(
    Guid Id,
    TicketStatus Status,
    Guid? AssignedTechnicianId,
    string? ResolutionSummary,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt);

public sealed record TicketResolutionReviewResult(
    TicketResolutionReviewOutcome Outcome,
    TicketResolutionReviewDetails? Ticket = null);
