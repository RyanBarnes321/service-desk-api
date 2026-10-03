using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Contracts.Tickets;

public sealed record ListTicketResponse(
    Guid Id,
    string Title,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    Guid CreatedByUserId,
    Guid? AssignedTechnicianId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
