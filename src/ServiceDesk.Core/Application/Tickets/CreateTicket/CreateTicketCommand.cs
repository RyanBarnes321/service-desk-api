using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.CreateTicket;

public sealed record CreateTicketCommand(
    string Title,
    string Description,
    TicketCategory Category,
    TicketPriority Priority,
    Guid CreatedByUserId);
