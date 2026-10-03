using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Contracts.Tickets;

public sealed record CreateTicketResponse(
    Guid Id,
    string Title,
    string Description,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt);
