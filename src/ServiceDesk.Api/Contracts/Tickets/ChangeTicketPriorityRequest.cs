using ServiceDesk.Core.Enums;

namespace ServiceDesk.Api.Contracts.Tickets;

public sealed record ChangeTicketPriorityRequest(TicketPriority? Priority);
