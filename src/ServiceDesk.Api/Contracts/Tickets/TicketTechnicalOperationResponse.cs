using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Contracts.Tickets;

public sealed record TicketTechnicalOperationResponse(
    Guid Id,
    TicketStatus Status,
    TicketPriority Priority,
    Guid? AssignedTechnicianId,
    string? ResolutionSummary,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt);
