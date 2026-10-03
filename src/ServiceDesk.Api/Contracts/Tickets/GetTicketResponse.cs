using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Contracts.Tickets;

public sealed record GetTicketResponse(
    Guid Id,
    string Title,
    string Description,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    Guid CreatedByUserId,
    Guid? AssignedTechnicianId,
    string? ResolutionSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt);
