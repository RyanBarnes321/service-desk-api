using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Contracts.Tickets;

public sealed record TicketResolutionReviewResponse(
    Guid Id,
    TicketStatus Status,
    Guid? AssignedTechnicianId,
    string? ResolutionSummary,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt);
