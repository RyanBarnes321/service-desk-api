using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.ListTickets;

public sealed record ListTicketItem(
    Guid Id,
    string Title,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    Guid CreatedByUserId,
    Guid? AssignedTechnicianId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
