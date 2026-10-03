using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.GetTicket;

public sealed record GetTicketResult(
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
