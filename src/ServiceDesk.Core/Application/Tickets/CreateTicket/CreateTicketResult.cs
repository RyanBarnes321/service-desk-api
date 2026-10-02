using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.CreateTicket;

public sealed record CreateTicketResult(
    Guid Id,
    string Title,
    string Description,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt)
{
    internal static CreateTicketResult FromTicket(Ticket ticket)
    {
        return new CreateTicketResult(
            ticket.Id,
            ticket.Title,
            ticket.Description,
            ticket.Category,
            ticket.Priority,
            ticket.Status,
            ticket.CreatedByUserId,
            ticket.CreatedAt);
    }
}
