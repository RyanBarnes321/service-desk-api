using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Contracts.Tickets;

public sealed record CreateTicketRequest(
    string Title,
    string Description,
    TicketCategory Category,
    TicketPriority Priority);
