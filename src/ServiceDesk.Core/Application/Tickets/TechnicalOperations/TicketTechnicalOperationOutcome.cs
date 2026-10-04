namespace ServiceDesk.Core.Application.Tickets.TechnicalOperations;

public enum TicketTechnicalOperationOutcome
{
    Success = 0,
    Forbidden = 1,
    TicketNotFound = 2,
    InvalidState = 3,
    ConcurrencyConflict = 4
}
