using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.TechnicalOperations;

public sealed record TicketTechnicalOperationDetails(
    Guid Id,
    TicketStatus Status,
    TicketPriority Priority,
    Guid? AssignedTechnicianId,
    string? ResolutionSummary,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt);
