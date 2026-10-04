namespace ServiceDesk.Core.Application.Tickets.TechnicalOperations;

public sealed record TicketTechnicalOperationResult(
    TicketTechnicalOperationOutcome Outcome,
    TicketTechnicalOperationDetails? Ticket = null);
