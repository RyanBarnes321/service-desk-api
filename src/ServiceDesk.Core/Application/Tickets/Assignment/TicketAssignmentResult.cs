namespace ServiceDesk.Core.Application.Tickets.Assignment;

public sealed record TicketAssignmentResult(
    TicketAssignmentOutcome Outcome,
    TicketAssignmentDetails? Ticket = null);
